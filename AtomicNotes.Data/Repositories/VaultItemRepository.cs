using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Data.Repositories;

public sealed class VaultItemRepository : IVaultItemRepository
{
    private readonly IDbConnectionFactory _factory;

    public VaultItemRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<long> InsertAsync(VaultItem item, CancellationToken cancellationToken = default)
    {
        if (item.Depth < 1 || item.Depth > AppConstants.MaxTreeDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.Depth,
                $"Tree depth must be between 1 and {AppConstants.MaxTreeDepth}.");
        }

        using var connection = _factory.Create();
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO vault_items (vault_path, rel_path, item_type, title, depth, parent_id)
                VALUES (@VaultPath, @RelPath, @ItemType, @Title, @Depth, @ParentId);
                SELECT last_insert_rowid();
                """,
                item,
                cancellationToken: cancellationToken));
    }
}
