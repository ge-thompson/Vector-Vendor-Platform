using Microsoft.EntityFrameworkCore;
using Vector.VVI.Admin.Data;
using Vector.VVI.Admin.Data.Entities;

namespace Vector.VVI.Admin.Auth.Services;

public interface IUserService
{
    /// <summary>Return the active user if email+password match, else null.</summary>
    Task<AdminUser?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);

    Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<AdminUser?> FindByIdAsync(int userId, CancellationToken ct = default);
    Task<List<AdminUser>> ListAsync(CancellationToken ct = default);

    /// <summary>Create a user with a temporary random password (admin sets real one via token flow later).</summary>
    Task<AdminUser> CreateAsync(string email, string? name, string role, string createdBy, CancellationToken ct = default);

    Task SetPasswordAsync(int userId, string newPassword, CancellationToken ct = default);
    Task RecordLoginAsync(int userId, CancellationToken ct = default);
}

public sealed class UserService : IUserService
{
    private readonly VviDbContext _db;
    private readonly IPasswordHasher _hasher;

    public UserService(VviDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<AdminUser?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await FindByEmailAsync(email, ct);
        if (user is null || !user.IsActive) return null;
        return _hasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        _db.AdminUsers.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<AdminUser?> FindByIdAsync(int userId, CancellationToken ct = default) =>
        _db.AdminUsers.FirstOrDefaultAsync(u => u.UserID == userId, ct);

    public Task<List<AdminUser>> ListAsync(CancellationToken ct = default) =>
        _db.AdminUsers.OrderBy(u => u.Email).ToListAsync(ct);

    public async Task<AdminUser> CreateAsync(string email, string? name, string role, string createdBy, CancellationToken ct = default)
    {
        // Temp password is random and unusable until the user sets their own.
        var temp = Guid.NewGuid().ToString("N");
        var user = new AdminUser
        {
            Email = email,
            Name = name,
            Role = role,
            IsActive = true,
            PasswordHash = _hasher.Hash(temp),
            CreatedBy = createdBy
        };
        _db.AdminUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        var user = await FindByIdAsync(userId, ct)
                   ?? throw new InvalidOperationException($"User {userId} not found.");
        user.PasswordHash = _hasher.Hash(newPassword);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RecordLoginAsync(int userId, CancellationToken ct = default)
    {
        var user = await FindByIdAsync(userId, ct);
        if (user is null) return;
        user.LastLoginUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
