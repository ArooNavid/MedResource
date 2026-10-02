using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IVaultItemRepository
{
    Task<long> InsertAsync(VaultItem item, CancellationToken cancellationToken = default);
}
