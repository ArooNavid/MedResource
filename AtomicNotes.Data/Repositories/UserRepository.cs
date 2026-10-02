using System.Globalization;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _factory;

    public UserRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<long> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO users (username, password_hash, salt, display_name, vault_path, staging_path, role, is_active)
                VALUES (@Username, @PasswordHash, @Salt, @DisplayName, @VaultPath, @StagingPath, @Role, @IsActive);
                SELECT last_insert_rowid();
                """,
                new
                {
                    user.Username,
                    user.PasswordHash,
                    user.Salt,
                    user.DisplayName,
                    user.VaultPath,
                    user.StagingPath,
                    Role = user.Role.ToString(),
                    IsActive = user.IsActive ? 1 : 0
                },
                cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            new CommandDefinition(SelectSql + " WHERE id = @Id", new { Id = id }, cancellationToken: cancellationToken));
        return row?.ToUser();
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            new CommandDefinition(SelectSql + " WHERE username = @Username", new { Username = username }, cancellationToken: cancellationToken));
        return row?.ToUser();
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(1) FROM users", cancellationToken: cancellationToken));
    }

    public async Task UpdateLastLoginAsync(long userId, DateTime utc, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE users SET last_login_at = @At WHERE id = @Id",
                new { Id = userId, At = utc.ToString("o", CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken));
    }

    public async Task<UserRole> GetRoleAsync(long userId, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var role = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition("SELECT role FROM users WHERE id = @Id", new { Id = userId }, cancellationToken: cancellationToken));
        if (role is null)
            throw new InvalidOperationException($"User {userId} does not exist.");
        return ParseRole(role);
    }

    internal static UserRole ParseRole(string role) =>
        string.Equals(role, AppConstants.RoleAdmin, StringComparison.Ordinal)
            ? UserRole.Admin
            : UserRole.User;

    private const string SelectSql = """
        SELECT id, username, password_hash, salt, display_name, vault_path, staging_path,
               role, is_active, created_at, last_login_at
        FROM users
        """;

    private sealed class UserRow
    {
        public long id { get; set; }
        public string username { get; set; } = string.Empty;
        public string password_hash { get; set; } = string.Empty;
        public string salt { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string vault_path { get; set; } = string.Empty;
        public string staging_path { get; set; } = string.Empty;
        public string role { get; set; } = AppConstants.RoleUser;
        public int is_active { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string? last_login_at { get; set; }

        public User ToUser() => new()
        {
            Id = id,
            Username = username,
            PasswordHash = password_hash,
            Salt = salt,
            DisplayName = display_name,
            VaultPath = vault_path,
            StagingPath = staging_path,
            Role = ParseRole(role),
            IsActive = is_active != 0,
            CreatedAt = DateTime.Parse(created_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            LastLoginAt = last_login_at is null
                ? null
                : DateTime.Parse(last_login_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }
}
