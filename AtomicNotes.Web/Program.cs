using System.Security.Claims;
using System.Text.Json;
using AtomicNotes.App;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using AtomicNotes.Data;
using AtomicNotes.Data.Repositories;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var settings = new SettingsService();
settings.Load();
Directory.CreateDirectory(settings.Current.VaultPath);
Directory.CreateDirectory(settings.Current.BackupPath);
Directory.CreateDirectory(Path.GetDirectoryName(settings.Current.DatabasePath)!);

var database = new SqliteConnectionFactory(settings.Current.DatabasePath);
builder.Services.AddSingleton<ISettingsService>(settings);
builder.Services.AddSingleton<IDbConnectionFactory>(database);
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ITehranClockService, TehranClockService>();
builder.Services.AddSingleton<IActivityStatsService, ActivityStatsService>();
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<ITagService, TagService>();
builder.Services.AddSingleton<IAliasService, AliasService>();
builder.Services.AddSingleton<INoteLinkService, NoteLinkService>();
builder.Services.AddSingleton<IGraphService, GraphService>();
builder.Services.AddSingleton<IPdfExportService, PdfExportService>();
builder.Services.AddSingleton<VaultWriteGuard>();
builder.Services.AddSingleton<NoteService>();
builder.Services.AddSingleton<INoteService>(sp => sp.GetRequiredService<NoteService>());
builder.Services.AddSingleton<IPdfImportService, PdfImportService>();
builder.Services.AddSingleton<IBackupService, BackupService>();
builder.Services.AddSingleton<IBackupSchedulerService, BackupSchedulerService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<IObsidianSyncService, ObsidianSyncService>();
builder.Services.AddSingleton<ITemplateService, TemplateService>();
builder.Services.AddSingleton<IDailyNoteService, DailyNoteService>();
builder.Services.AddSingleton<ITaskService, TaskService>();
builder.Services.AddSingleton<IMarkdownPreviewService, MarkdownPreviewService>();
builder.Services.AddSingleton<INoteMarkdownService, NoteMarkdownService>();
builder.Services.AddSingleton<VaultWatcherService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "atomicnotes";
        options.Cookie.HttpOnly = true;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
await app.Services.GetRequiredService<IPdfImportService>().RecoverStagedAsync();
await app.Services.GetRequiredService<IObsidianSyncService>().SyncAllAsync();
app.Services.GetRequiredService<VaultWatcherService>().Start();
app.Services.GetRequiredService<IBackupSchedulerService>().Start();
app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<VaultWatcherService>().Dispose();
    app.Services.GetRequiredService<IBackupSchedulerService>().DisposeAsync().AsTask().GetAwaiter().GetResult();
    app.Services.GetRequiredService<ITehranClockService>().Dispose();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapPost("/api/auth/register", async (RegisterBody body, AuthService auth, IActivityStatsService stats, HttpContext http) =>
{
    var result = await auth.RegisterAsync(body.Username ?? "", body.Password ?? "", body.DisplayName);
    if (!result.Success || result.User is null)
        return Results.BadRequest(new { error = result.Error });
    await stats.RecordLoginAsync(result.User.Id);
    await SignIn(http, result.User);
    return Results.Ok(PublicUser(result.User));
});

app.MapPost("/api/auth/login", async (LoginBody body, AuthService auth, IActivityStatsService stats, HttpContext http) =>
{
    var result = await auth.AuthenticateAsync(body.Username ?? "", body.Password ?? "");
    if (!result.Success || result.User is null)
        return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
    await stats.RecordLoginAsync(result.User.Id);
    await SignIn(http, result.User);
    return Results.Ok(PublicUser(result.User));
});

app.MapPost("/api/auth/logout", async (IActivityStatsService stats, HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated == true)
        await stats.RecordLogoutAsync(UserId(http.User));
    await http.SignOutAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/auth/me", (HttpContext http) => Results.Ok(new
{
    id = UserId(http.User),
    username = http.User.Identity?.Name,
    displayName = http.User.FindFirstValue("display"),
    role = http.User.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

app.MapGet("/api/clock", (ITehranClockService clock) =>
{
    var tehran = clock.TehranNow;
    return Results.Ok(new
    {
        tehranNow = tehran.ToString("yyyy/MM/dd HH:mm:ss"),
        tehranDate = clock.TehranDateString,
        utcNow = DateTime.UtcNow.ToString("o")
    });
}).RequireAuthorization();

app.MapGet("/api/dashboard", async (HttpContext http, IUserRepository users, IActivityStatsService stats, INoteService notes, ITehranClockService clock) =>
{
    var id = UserId(http.User);
    var dashboard = new DashboardViewModel(users, stats, id);
    var from = DateTime.UtcNow.Date.AddDays(-30);
    var to = DateTime.UtcNow.AddDays(1);
    await dashboard.LoadAsync(from, to);
    var recent = await notes.RecentAsync(8);
    var pinned = await notes.ListPinnedAsync();
    return Results.Ok(new
    {
        isAdmin = dashboard.IsAdmin,
        tehranNow = clock.TehranNow.ToString("yyyy/MM/dd HH:mm:ss"),
        tehranDate = clock.TehranDateString,
        today = dashboard.OwnToday,
        comparison = dashboard.Comparison,
        pinned,
        recent
    });
}).RequireAuthorization();

app.MapGet("/api/activity/report", async (long userId, HttpContext http, IActivityStatsService stats) =>
{
    try
    {
        var report = await stats.GetReportAsync(UserId(http.User), userId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1));
        return Results.Ok(report);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapGet("/api/notes", async (string? tagIds, string? sort, string? order, INoteService notes, ITagService tags) =>
{
    var list = await notes.ListAsync(NoteListSortParser.ParseSort(sort), NoteListSortParser.ParseAscending(order));
    if (!string.IsNullOrWhiteSpace(tagIds))
    {
        var ids = tagIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToArray();
        var allowed = (await tags.GetNoteIdsByTagsAsync(ids)).ToHashSet();
        list = list.Where(note => allowed.Contains((int)note.Id)).ToList();
    }
    return Results.Ok(list);
}).RequireAuthorization();

app.MapGet("/api/notes/random", async (INoteService notes) =>
{
    var note = await notes.GetRandomAsync();
    return note is null ? Results.NotFound(new { error = "یادداشتی برای انتخاب تصادفی نیست." }) : Results.Ok(note);
}).RequireAuthorization();

app.MapGet("/api/notes/{id:long}", async (long id, INoteService notes, ITagService tags, IAliasService aliases, INoteLinkService links) =>
{
    var note = await notes.GetAsync(id);
    if (note is null)
        return Results.NotFound();
    var noteTags = await tags.GetTagsForNoteAsync((int)id);
    var noteAliases = await aliases.GetAliasesForNoteAsync((int)id);
    var outgoing = await links.GetOutgoingLinksAsync((int)id);
    var backlinks = await links.GetBacklinksAsync((int)id);
    return Results.Ok(new { note, tags = noteTags, aliases = noteAliases, outgoing, backlinks });
}).RequireAuthorization();

app.MapPost("/api/notes", async (NoteBody body, HttpContext http, INoteService notes) =>
{
    try
    {
        var note = await notes.CreateAsync(UserId(http.User), body.Title ?? "", body.Content ?? "", body.ParentNoteId, body.Tags ?? Array.Empty<string>(), body.Aliases ?? Array.Empty<string>());
        return Results.Ok(note);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/notes/{targetId:long}/merge/{sourceId:long}", async (long targetId, long sourceId, HttpContext http, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.MergeAsync(targetId, sourceId, UserId(http.User)));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/notes/{id:long}/duplicate", async (long id, HttpContext http, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.DuplicateAsync(id, UserId(http.User)));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/notes/{id:long}/pin", async (long id, PinBody body, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.SetPinnedAsync(id, body.Pinned));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/notes/{id:long}/sync-path", async (long id, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.SyncRelPathToTitleAsync(id));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/notes/{id:long}/parent", async (long id, ParentBody body, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.SetParentAsync(id, body.ParentNoteId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/trash", async (INoteService notes) => Results.Ok(await notes.ListTrashAsync())).RequireAuthorization();

app.MapPost("/api/trash/restore-all", async (INoteService notes) =>
{
    var count = await notes.RestoreAllTrashAsync();
    return Results.Ok(new { count });
}).RequireAuthorization();

app.MapDelete("/api/trash", async (INoteService notes) =>
{
    var count = await notes.EmptyTrashAsync();
    return Results.Ok(new { count });
}).RequireAuthorization();

app.MapPost("/api/trash/{id:long}/restore", async (long id, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.RestoreAsync(id));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapDelete("/api/trash/{id:long}", async (long id, INoteService notes) =>
{
    await notes.PurgeAsync(id);
    return Results.Ok();
}).RequireAuthorization();

app.MapPut("/api/notes/{id:long}", async (long id, NoteBody body, HttpContext http, INoteService notes) =>
{
    try
    {
        var note = await notes.UpdateAsync(id, UserId(http.User), body.Title ?? "", body.Content ?? "", body.Tags ?? Array.Empty<string>(), body.Aliases ?? Array.Empty<string>());
        return Results.Ok(note);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/templates", async (ITemplateService templates) => Results.Ok(await templates.ListAsync())).RequireAuthorization();

app.MapPost("/api/templates", async (TemplateBody body, ITemplateService templates) =>
{
    try
    {
        var template = await templates.CreateAsync(body.Title ?? "", body.Content ?? "", body.Tags ?? Array.Empty<string>());
        return Results.Ok(template);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapDelete("/api/templates", async (string name, ITemplateService templates) =>
{
    try
    {
        await templates.DeleteAsync(name);
        return Results.Ok();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/notes/{id:long}/template", async (long id, ApplyTemplateBody body, HttpContext http, ITemplateService templates) =>
{
    try
    {
        var note = await templates.ApplyAsync(id, UserId(http.User), body.Name ?? "");
        return Results.Ok(note);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/markdown/preview", async (PreviewBody body, IMarkdownPreviewService preview) =>
    Results.Ok(new { html = await preview.RenderAsync(body.Content ?? string.Empty, body.NoteId) })).RequireAuthorization();

app.MapGet("/api/tasks", async (bool? open, ITaskService tasks) =>
    Results.Ok(await tasks.ListAsync(open))).RequireAuthorization();

app.MapPost("/api/tasks/toggle", async (TaskToggleBody body, HttpContext http, ITaskService tasks) =>
{
    try
    {
        var note = await tasks.ToggleAsync(body.NoteId, UserId(http.User), body.LineIndex, body.Done);
        return Results.Ok(note);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/daily/month", async (int? year, int? month, IDailyNoteService daily, ITehranClockService clock) =>
{
    try
    {
        var tehran = clock.TehranNow;
        var y = year ?? tehran.Year;
        var m = month ?? tehran.Month;
        return Results.Ok(await daily.GetMonthAsync(y, m));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/daily", async (DailyRequest? body, HttpContext http, IDailyNoteService daily) =>
{
    try
    {
        var result = await daily.OpenAsync(UserId(http.User), body?.Date);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapDelete("/api/notes/{id:long}", async (long id, INoteService notes) =>
{
    await notes.DeleteAsync(id);
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/notes/{id:long}/markdown", async (long id, INoteMarkdownService markdown) =>
{
    try
    {
        var exported = await markdown.ExportAsync(id);
        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(exported.Content),
            "text/markdown; charset=utf-8",
            exported.FileName);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/notes/import-markdown", async (HttpRequest request, HttpContext http, INoteMarkdownService markdown) =>
{
    var file = request.Form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "فایل .md انتخاب نشده است." });
    try
    {
        await using var stream = file.OpenReadStream();
        var note = await markdown.ImportAsync(UserId(http.User), file.FileName, stream);
        return Results.Ok(note);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/notes/{id:long}/pdf", async (long id, INoteService notes, IPdfExportService pdf) =>
{
    var note = await notes.GetAsync(id);
    if (note is null)
        return Results.NotFound();
    var path = Path.Combine(Path.GetTempPath(), $"atomicnotes-{id}-{Guid.NewGuid():N}.pdf");
    await pdf.ExportAsync(note.Title, $"# {note.Title}\n\n{note.Content}", path);
    var bytes = await File.ReadAllBytesAsync(path);
    File.Delete(path);
    var fileName = MarkdownFiles.SanitizeFileName(note.Title) + ".pdf";
    return Results.File(bytes, "application/pdf", fileName);
}).RequireAuthorization();

app.MapGet("/api/search", async (string? q, ISearchService search) =>
    Results.Ok(await search.SearchAsync(q ?? ""))).RequireAuthorization();

app.MapGet("/api/tags", async (ITagService tags) => Results.Ok(await tags.GetAllTagsAsync())).RequireAuthorization();

app.MapDelete("/api/tags/{id:int}", async (int id, ITagService tags) =>
{
    await tags.DeleteTagAsync(id);
    return Results.Ok();
}).RequireAuthorization();

app.MapPut("/api/tags/{id:int}/color", async (int id, ColorBody body, ITagService tags) =>
{
    await tags.UpdateTagColorAsync(id, body.ColorHex ?? "#6C757D");
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/links/unresolved", async (INoteLinkService links) =>
    Results.Ok(await links.ListUnresolvedAsync())).RequireAuthorization();

app.MapPost("/api/links/create-target", async (LinkTargetBody body, HttpContext http, INoteService notes) =>
{
    try
    {
        return Results.Ok(await notes.CreateFromLinkTargetAsync(UserId(http.User), body.Target ?? ""));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/graph", async (IGraphService graph) =>
{
    var (nodes, edges) = await graph.LoadGraphAsync();
    return Results.Ok(new
    {
        nodes = nodes.Select(node => new { node.NoteId, node.Title, node.X, node.Y, node.Degree, radius = node.Radius }),
        edges = edges.Select(edge => new { edge.SourceNoteId, edge.TargetNoteId, edge.IsResolved })
    });
}).RequireAuthorization();

app.MapGet("/api/sync", (IObsidianSyncService sync) => Results.Ok(sync.LastReport)).RequireAuthorization();

app.MapPost("/api/sync", async (IObsidianSyncService sync) => Results.Ok(await sync.SyncAllAsync())).RequireAuthorization();

app.MapGet("/api/settings", (ISettingsService settingsService) =>
{
    var current = settingsService.Load();
    return Results.Ok(new
    {
        current.VaultPath,
        current.BackupPath,
        current.DatabasePath,
        current.BackupIntervalHours,
        current.Theme,
        current.NotificationsEnabled,
        current.LastAutoBackupAt,
        settingsFilePath = settingsService.SettingsFilePath
    });
}).RequireAuthorization();

app.MapPut("/api/settings", async (SettingsBody body, ISettingsService settingsService, IBackupSchedulerService scheduler, VaultWatcherService watcher) =>
{
    if (string.IsNullOrWhiteSpace(body.VaultPath))
        return Results.BadRequest(new { error = "مسیر Vault نمی‌تواند خالی باشد." });
    if (body.BackupIntervalHours is < AppConstants.MinBackupIntervalHours or > AppConstants.MaxBackupIntervalHours)
        return Results.BadRequest(new { error = "بازه پشتیبان‌گیری باید بین ۱ تا ۱۶۸ ساعت باشد." });

    var current = settingsService.Load();
    settingsService.Save(new AppSettings
    {
        VaultPath = body.VaultPath.Trim(),
        BackupPath = body.BackupPath?.Trim() ?? "",
        DatabasePath = current.DatabasePath,
        BackupIntervalHours = body.BackupIntervalHours,
        Theme = string.IsNullOrWhiteSpace(body.Theme) ? "System" : body.Theme,
        NotificationsEnabled = body.NotificationsEnabled,
        LastAutoBackupAt = current.LastAutoBackupAt
    });
    Directory.CreateDirectory(settingsService.Current.VaultPath);
    if (!string.IsNullOrWhiteSpace(settingsService.Current.BackupPath))
        Directory.CreateDirectory(settingsService.Current.BackupPath);
    watcher.Start();
    await scheduler.RestartAsync();
    return Results.Ok(new { message = "تنظیمات ذخیره شد." });
}).RequireAuthorization();

app.MapGet("/api/backups", async (IBackupService backups) => Results.Ok(await backups.ListBackupsAsync())).RequireAuthorization();

app.MapPost("/api/backups", async (IBackupService backups) =>
{
    var result = await backups.CreateBackupAsync();
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization();

app.MapPost("/api/backups/restore", async (RestoreBody body, IBackupService backups) =>
{
    var listed = await backups.ListBackupsAsync();
    var match = listed.FirstOrDefault(item => item.FilePath == body.FilePath || item.FileName == body.FilePath);
    if (match is null)
        return Results.NotFound(new { error = "Backup file not found." });
    var result = await backups.RestoreBackupAsync(match);
    return result.Success
        ? Results.Ok(new { message = "بازیابی با موفقیت انجام شد. لطفاً برنامه را مجدداً راه‌اندازی کنید.", result.Success })
        : Results.BadRequest(new { error = result.Error });
}).RequireAuthorization();

app.MapDelete("/api/backups", async (string file, IBackupService backups) =>
{
    var listed = await backups.ListBackupsAsync();
    var match = listed.FirstOrDefault(item => item.FilePath == file || item.FileName == file);
    if (match is null)
        return Results.NotFound();
    await backups.DeleteBackupAsync(match);
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/api/import", async (HttpRequest request, HttpContext http, IPdfImportService imports) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "فایل PDF لازم است." });
    var form = await request.ReadFormAsync();
    var outcomes = new List<ImportOutcome>();
    foreach (var file in form.Files)
    {
        await using var stream = file.OpenReadStream();
        outcomes.Add(await imports.ImportAsync(UserId(http.User), file.FileName, stream));
    }
    return Results.Ok(outcomes);
}).RequireAuthorization();

app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();

static long UserId(ClaimsPrincipal user) =>
    long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

static object PublicUser(AtomicNotes.Core.Models.User user) => new
{
    id = user.Id,
    username = user.Username,
    displayName = user.DisplayName,
    role = user.Role.ToString()
};

static async Task SignIn(HttpContext http, AtomicNotes.Core.Models.User user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role.ToString()),
        new("display", user.DisplayName)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identity));
}

internal sealed record RegisterBody(string? Username, string? Password, string? DisplayName);
internal sealed record LoginBody(string? Username, string? Password);
internal sealed record NoteBody(string? Title, string? Content, long? ParentNoteId, string[]? Tags, string[]? Aliases);

internal sealed record DailyRequest(string? Date);

internal sealed record TemplateBody(string? Title, string? Content, string[]? Tags);

internal sealed record ApplyTemplateBody(string? Name);

internal sealed record LinkTargetBody(string? Target);

internal sealed record TaskToggleBody(long NoteId, int LineIndex, bool Done);

internal sealed record PreviewBody(string? Content, long? NoteId);

internal sealed record PinBody(bool Pinned);

internal sealed record ParentBody(long? ParentNoteId);
internal sealed record ColorBody(string? ColorHex);
internal sealed record SettingsBody(string? VaultPath, string? BackupPath, int BackupIntervalHours, string? Theme, bool NotificationsEnabled);
internal sealed record RestoreBody(string? FilePath);
