using AtomicNotes.Core.Interfaces;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;

namespace AtomicNotes.Tests.Support;

internal static class NoteTestFactory
{
    public static NoteService Create(
        IDbConnectionFactory factory,
        SettingsService settings,
        ActivityStatsService stats,
        TagService? tags = null,
        VaultWriteGuard? guard = null)
    {
        var bundle = CreateBundle(factory, settings, stats, tags, guard);
        return bundle.notes;
    }

    public static (NoteService notes, NoteLinkService links, AliasService aliases, TagService tags, VaultWriteGuard guard) CreateBundle(
        IDbConnectionFactory factory,
        SettingsService settings,
        ActivityStatsService stats,
        TagService? tags = null,
        VaultWriteGuard? guard = null)
    {
        tags ??= new TagService(factory);
        guard ??= new VaultWriteGuard();
        var aliases = new AliasService(factory);
        var links = new NoteLinkService(factory, aliases);
        var notes = new NoteService(factory, settings, tags, links, aliases, stats, guard);
        return (notes, links, aliases, tags, guard);
    }
}
