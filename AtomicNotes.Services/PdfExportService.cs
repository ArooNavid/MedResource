using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace AtomicNotes.Services;

public sealed class PdfExportService : AtomicNotes.Core.Interfaces.IPdfExportService
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    static PdfExportService()
    {
        if (GlobalFontSettings.FontResolver is not PersianFontResolver)
            GlobalFontSettings.FontResolver = new PersianFontResolver();
    }

    public Task ExportAsync(string title, string markdownContent, string outputPath, CancellationToken ct = default)
        => Task.Run(() => Generate(title, markdownContent, outputPath), ct);

    private static void Generate(string title, string markdown, string path)
    {
        var ast = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        using var doc = new PdfDocument();
        doc.Info.Title = title;
        doc.Info.Creator = "AtomicNotes";
        var ctx = new RenderContext(doc);
        ctx.DrawTitle(title);
        foreach (var block in ast)
            RenderBlock(block, ctx, indentLevel: 0);

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        doc.Save(path);
    }

    private static void RenderBlock(Block block, RenderContext ctx, int indentLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                ctx.DrawHeading(InlineText(heading.Inline), heading.Level);
                break;
            case ParagraphBlock paragraph:
                ctx.DrawParagraph(InlineText(paragraph.Inline), indentLevel);
                break;
            case FencedCodeBlock code:
                ctx.DrawCode(code.Lines.ToString());
                break;
            case CodeBlock code:
                ctx.DrawCode(code.Lines.ToString());
                break;
            case ListBlock list:
                var index = 1;
                foreach (var item in list)
                {
                    if (item is ListItemBlock listItem)
                    {
                        var marker = list.IsOrdered ? $"{index}." : "•";
                        var text = string.Join(" ", listItem.OfType<ParagraphBlock>().Select(p => InlineText(p.Inline)));
                        ctx.DrawListItem(marker, text, indentLevel);
                        foreach (var child in listItem.Where(child => child is not ParagraphBlock))
                            RenderBlock(child, ctx, indentLevel + 1);
                        index++;
                    }
                }
                ctx.AddSpace(4);
                break;
            case QuoteBlock quote:
                var start = ctx.CurrentY;
                foreach (var child in quote)
                    RenderBlock(child, ctx, indentLevel + 1);
                ctx.DrawQuoteBar(start);
                break;
            case ThematicBreakBlock:
                ctx.DrawRule();
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    RenderBlock(child, ctx, indentLevel);
                break;
        }
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;
        return string.Concat(inline.Select(child => child switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            LineBreakInline => "\n",
            EmphasisInline emphasis => InlineText(emphasis),
            LinkInline link => InlineText(link),
            ContainerInline container => InlineText(container),
            _ => string.Empty
        }));
    }

    private sealed class RenderContext
    {
        private const double Margin = 72;
        private readonly PdfDocument _doc;
        private PdfPage _page;
        private XGraphics _gfx;
        private readonly XFont _body = new("Noto Naskh Arabic", 11, XFontStyle.Regular);
        private readonly XFont _bold = new("Noto Naskh Arabic", 11, XFontStyle.Bold);
        private readonly XFont _code = new("DejaVu Sans Mono", 10, XFontStyle.Regular);
        private readonly XFont[] _headings =
        [
            new("Noto Naskh Arabic", 22, XFontStyle.Bold),
            new("Noto Naskh Arabic", 18, XFontStyle.Bold),
            new("Noto Naskh Arabic", 15, XFontStyle.Bold),
            new("Noto Naskh Arabic", 13, XFontStyle.Bold)
        ];

        public double CurrentY { get; private set; }

        public RenderContext(PdfDocument doc)
        {
            _doc = doc;
            _page = doc.AddPage();
            _gfx = XGraphics.FromPdfPage(_page);
            CurrentY = Margin;
        }

        public void DrawTitle(string title) => DrawHeading(title, 1);

        public void DrawHeading(string text, int level)
        {
            var font = _headings[Math.Clamp(level, 1, 4) - 1];
            Ensure(font.Height + 20);
            CurrentY += level == 1 ? 8 : 6;
            _gfx.DrawString(text, font, XBrushes.Black, new XRect(Margin, CurrentY, ContentWidth, font.Height), XStringFormats.TopLeft);
            CurrentY += font.Height + 4;
            if (level <= 2)
            {
                _gfx.DrawLine(new XPen(XColors.Gray, level == 1 ? 1.5 : 0.8), Margin, CurrentY, _page.Width - Margin, CurrentY);
                CurrentY += 6;
            }
        }

        public void DrawParagraph(string text, int indent)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var x = Margin + indent * 20;
            foreach (var line in Wrap(text, _body, ContentWidth - indent * 20))
            {
                Ensure(_body.Height + 4);
                _gfx.DrawString(line, _body, XBrushes.Black, new XRect(x, CurrentY, ContentWidth, _body.Height), XStringFormats.TopLeft);
                CurrentY += _body.Height + 2;
            }
            CurrentY += 4;
        }

        public void DrawCode(string code)
        {
            var lines = code.TrimEnd().Split('\n');
            var height = lines.Length * (_code.Height + 2) + 12;
            Ensure(height + 8);
            CurrentY += 6;
            _gfx.DrawRoundedRectangle(new XSolidBrush(XColor.FromArgb(245, 245, 245)), Margin, CurrentY, ContentWidth, height, 4, 4);
            var y = CurrentY + 6;
            foreach (var line in lines)
            {
                _gfx.DrawString(line, _code, XBrushes.DarkRed, new XRect(Margin + 8, y, ContentWidth - 16, _code.Height), XStringFormats.TopLeft);
                y += _code.Height + 2;
            }
            CurrentY += height + 6;
        }

        public void DrawListItem(string marker, string text, int indent)
        {
            Ensure(_body.Height + 4);
            var x = Margin + indent * 20;
            _gfx.DrawString(marker, _bold, XBrushes.Black, new XRect(x, CurrentY, 18, _body.Height), XStringFormats.TopLeft);
            _gfx.DrawString(text, _body, XBrushes.Black, new XRect(x + 18, CurrentY, ContentWidth - indent * 20 - 18, _body.Height), XStringFormats.TopLeft);
            CurrentY += _body.Height + 2;
        }

        public void DrawQuoteBar(double startY)
        {
            _gfx.DrawLine(new XPen(XColors.Gray, 3), Margin, startY + 4, Margin, CurrentY);
            CurrentY += 4;
        }

        public void DrawRule()
        {
            Ensure(20);
            CurrentY += 8;
            _gfx.DrawLine(new XPen(XColor.FromArgb(160, 160, 160), 0.8), Margin, CurrentY, _page.Width - Margin, CurrentY);
            CurrentY += 8;
        }

        public void AddSpace(double amount) => CurrentY += amount;

        private double ContentWidth => _page.Width - Margin * 2;

        private void Ensure(double needed)
        {
            if (CurrentY + needed <= _page.Height - Margin)
                return;
            _gfx.Dispose();
            _page = _doc.AddPage();
            _gfx = XGraphics.FromPdfPage(_page);
            CurrentY = Margin;
        }

        private static IEnumerable<string> Wrap(string text, XFont font, double width)
        {
            var words = text.Replace("\r", "").Split(' ');
            var line = "";
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, font) > width && line.Length > 0)
                {
                    yield return line;
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            if (line.Length > 0)
                yield return line;
        }

        private static double Measure(string text, XFont font)
        {
            // Approximate width so wrapping does not depend on a live graphics context.
            return text.Length * font.Size * 0.5;
        }
    }

    private sealed class PersianFontResolver : IFontResolver
    {
        public string DefaultFontName => "Noto Naskh Arabic";

        public byte[] GetFont(string faceName)
        {
            var path = faceName switch
            {
                "NotoBold" => "/usr/share/fonts/truetype/noto/NotoNaskhArabic-Bold.ttf",
                "DejaVuMono" => "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
                _ => "/usr/share/fonts/truetype/noto/NotoNaskhArabic-Regular.ttf"
            };
            if (!File.Exists(path))
                path = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
            return File.ReadAllBytes(path);
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (familyName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
                familyName.Contains("Courier", StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo("DejaVuMono");
            return new FontResolverInfo(isBold ? "NotoBold" : "Noto");
        }
    }
}
