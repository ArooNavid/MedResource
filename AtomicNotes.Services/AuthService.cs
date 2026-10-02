using System.Security.Cryptography;
using System.Text;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

public sealed record AuthResult(bool Success, User? User, string? Error);

public sealed class AuthService
{
    private readonly IUserRepository _users;

    public AuthService(IUserRepository users)
    {
        _users = users;
    }

    public async Task<AuthResult> RegisterAsync(string username, string password, string? displayName, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length < 2)
            return new AuthResult(false, null, "نام کاربری باید حداقل ۲ نویسه باشد.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            return new AuthResult(false, null, "رمز عبور باید حداقل ۴ نویسه باشد.");
        if (await _users.GetByUsernameAsync(username, ct) is not null)
            return new AuthResult(false, null, "این نام کاربری قبلاً ثبت شده است.");

        var (hash, salt) = HashPassword(password);
        var isFirst = await _users.CountAsync(ct) == 0;
        var user = new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            PasswordHash = hash,
            Salt = salt,
            Role = isFirst ? UserRole.Admin : UserRole.User,
            IsActive = true
        };
        user.Id = await _users.CreateAsync(user, ct);
        return new AuthResult(true, user, null);
    }

    public async Task<AuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await _users.GetByUsernameAsync(username.Trim(), ct);
        if (user is null || !user.IsActive || !Verify(password, user.PasswordHash, user.Salt))
            return new AuthResult(false, null, "نام کاربری یا رمز عبور نادرست است.");

        await _users.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, ct);
        return new AuthResult(true, user, null);
    }

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var expected = Convert.FromBase64String(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
