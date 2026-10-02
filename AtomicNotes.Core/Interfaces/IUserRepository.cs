using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IUserRepository
{
    Task<long> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task UpdateLastLoginAsync(long userId, DateTime utc, CancellationToken cancellationToken = default);
    Task<UserRole> GetRoleAsync(long userId, CancellationToken cancellationToken = default);
}
