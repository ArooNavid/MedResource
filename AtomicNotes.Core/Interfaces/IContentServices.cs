using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 50, CancellationToken ct = default);
    Task RebuildIndexAsync(CancellationToken ct = default);
}

public interface ITagService
{
    Task<IReadOnlyList<Tag>> GetAllTagsAsync(CancellationToken ct = default);
    Task<Tag> GetOrCreateTagAsync(string name, CancellationToken ct = default);
    Task UpdateTagColorAsync(int tagId, string colorHex, CancellationToken ct = default);
    Task DeleteTagAsync(int tagId, CancellationToken ct = default);
    Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(int noteId, CancellationToken ct = default);
    Task SetTagsForNoteAsync(int noteId, IEnumerable<string> tagNames, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetNoteIdsByTagsAsync(IEnumerable<int> tagIds, CancellationToken ct = default);
}

public interface IAliasService
{
    Task<IReadOnlyList<string>> GetAliasesForNoteAsync(int noteId, CancellationToken ct = default);
    Task SetAliasesForNoteAsync(int noteId, IEnumerable<string> aliases, CancellationToken ct = default);
    Task<long?> ResolveNoteIdAsync(string target, CancellationToken ct = default);
}

public interface INoteLinkService
{
    Task RebuildLinksForNoteAsync(int sourceNoteId, string markdownContent, CancellationToken ct = default);
    Task<IReadOnlyList<NoteLink>> GetOutgoingLinksAsync(int noteId, CancellationToken ct = default);
    Task<IReadOnlyList<NoteLink>> GetBacklinksAsync(int noteId, CancellationToken ct = default);
    Task ResolveLinksForTitleAsync(string noteTitle, int noteId, CancellationToken ct = default);
    Task<int> GetBacklinkCountAsync(int noteId, CancellationToken ct = default);
    Task NullifyLinksForOldTitleAsync(string oldTitle, CancellationToken ct = default);
    Task RemapTargetNoteAsync(int oldTargetNoteId, int newTargetNoteId, CancellationToken ct = default);
}

public interface IGraphService
{
    Task<(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)> LoadGraphAsync(CancellationToken ct = default);
}

public interface IPdfExportService
{
    Task ExportAsync(string title, string markdownContent, string outputPath, CancellationToken ct = default);
}

public interface INoteService
{
    Task<IReadOnlyList<Note>> ListAsync(NoteListSort sort = NoteListSort.Updated, bool ascending = false, CancellationToken ct = default);
    Task<Note?> GetRandomAsync(CancellationToken ct = default);
    Task<Note?> GetAsync(long id, CancellationToken ct = default);
    Task<Note> CreateAsync(long ownerUserId, string title, string content, long? parentNoteId, IEnumerable<string> tags, IEnumerable<string>? aliases = null, CancellationToken ct = default);
    Task<Note> UpdateAsync(long id, long editorUserId, string title, string content, IEnumerable<string> tags, IEnumerable<string>? aliases = null, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListTrashAsync(CancellationToken ct = default);
    Task<Note> RestoreAsync(long id, CancellationToken ct = default);
    Task<int> RestoreAllTrashAsync(CancellationToken ct = default);
    Task PurgeAsync(long id, CancellationToken ct = default);
    Task<int> EmptyTrashAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Note>> RecentAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListPinnedAsync(CancellationToken ct = default);
    Task<Note> SetPinnedAsync(long id, bool pinned, CancellationToken ct = default);
    Task<Note> SetParentAsync(long id, long? parentNoteId, CancellationToken ct = default);
    Task<Note> SyncRelPathToTitleAsync(long id, CancellationToken ct = default);
    Task<Note> DuplicateAsync(long id, long ownerUserId, CancellationToken ct = default);
    Task<Note> MergeAsync(long targetId, long sourceId, long editorUserId, CancellationToken ct = default);
}

public interface IPdfImportService
{
    Task<ImportOutcome> ImportAsync(long userId, string fileName, Stream pdf, CancellationToken ct = default);
    Task RecoverStagedAsync(CancellationToken ct = default);
}

public interface ITemplateService
{
    Task<IReadOnlyList<NoteTemplate>> ListAsync(CancellationToken ct = default);
    Task<NoteTemplate> CreateAsync(string title, string content, IEnumerable<string> tags, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
    Task<RenderedTemplate> RenderAsync(string name, string? title, CancellationToken ct = default);
    Task<RenderedTemplate?> RenderDailyAsync(DateTime day, CancellationToken ct = default);
    Task<Note> ApplyAsync(long noteId, long editorUserId, string name, CancellationToken ct = default);
}

public interface INoteMarkdownService
{
    Task<MarkdownExportResult> ExportAsync(long noteId, CancellationToken ct = default);
    Task<Note> ImportAsync(long ownerUserId, string fileName, Stream markdown, CancellationToken ct = default);
}

public interface IMarkdownPreviewService
{
    Task<string> RenderAsync(string markdown, long? noteId = null, CancellationToken ct = default);
}

public interface ITaskService
{
    Task<TaskListSummary> ListAsync(bool? openOnly = null, CancellationToken ct = default);
    Task<Note> ToggleAsync(long noteId, long editorUserId, int lineIndex, bool done, CancellationToken ct = default);
}

public interface IDailyNoteService
{
    /// <summary>
    /// Opens the daily note for a Tehran date. A missing date means today.
    /// The same path is never created twice and an existing file is left as it is.
    /// </summary>
    Task<DailyNoteResult> OpenAsync(long ownerUserId, string? tehranDate = null, CancellationToken ct = default);

    /// <summary>Stage 24: month grid of daily notes keyed by Tehran yyyy-MM-dd.</summary>
    Task<DailyJournalMonth> GetMonthAsync(int year, int month, CancellationToken ct = default);
}

public interface IObsidianSyncService
{
    SyncReport? LastReport { get; }
    Task<SyncReport> SyncAllAsync(CancellationToken ct = default);
    Task PullFileAsync(string fullPath, CancellationToken ct = default);
    Task OnFileMissingAsync(string fullPath, CancellationToken ct = default);
}
