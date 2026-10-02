namespace AtomicNotes.Services;

/// <summary>Stage 46: canonical [[wikilink]] text for clipboard and editor insert.</summary>
public static class WikilinkMarkup
{
    public static string Format(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("عنوان پیوند خالی است.", nameof(target));

        return "[[" + target.Trim() + "]]";
    }
}
