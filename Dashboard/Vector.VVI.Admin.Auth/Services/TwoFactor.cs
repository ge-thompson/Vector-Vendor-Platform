using Microsoft.EntityFrameworkCore;
using Vector.VVI.Admin.Data;
using Vector.VVI.Admin.Data.Entities;

namespace Vector.VVI.Admin.Auth.Services;

public enum TwoFactorResult { Success, Invalid, Expired, TooManyAttempts, NoActiveCode }

public interface ITwoFactorService
{
    /// <summary>Generate + email a 6-digit code for the user. Returns the code (dev stub logs it).</summary>
    Task<string> IssueCodeAsync(int userId, string email, CancellationToken ct = default);

    /// <summary>Verify a submitted code against the newest active challenge.</summary>
    Task<TwoFactorResult> VerifyCodeAsync(int userId, string submittedCode, CancellationToken ct = default);
}

/// <summary>
/// 2FA over dbo.AdminTwoFactorCodes. Codes are 6 digits, hashed (never stored
/// plain), 10-minute expiry, max 3 attempts.
/// </summary>
public sealed class TwoFactorService : ITwoFactorService
{
    private const int ExpiryMinutes = 10;
    private const int MaxAttempts = 3;

    private readonly VviDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;

    public TwoFactorService(VviDbContext db, IPasswordHasher hasher, IEmailSender email)
    {
        _db = db;
        _hasher = hasher;
        _email = email;
    }

    public async Task<string> IssueCodeAsync(int userId, string email, CancellationToken ct = default)
    {
        var code = Random.Shared.Next(0, 1_000_000).ToString("D6");

        _db.AdminTwoFactorCodes.Add(new AdminTwoFactorCode
        {
            UserID = userId,
            CodeHash = _hasher.Hash(code),
            ExpiresUtc = DateTime.UtcNow.AddMinutes(ExpiryMinutes),
            Attempts = 0
        });
        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(email, "Your VVI Admin verification code",
            $"Your verification code is {code}. It expires in {ExpiryMinutes} minutes.", ct);

        return code;
    }

    public async Task<TwoFactorResult> VerifyCodeAsync(int userId, string submittedCode, CancellationToken ct = default)
    {
        var challenge = await _db.AdminTwoFactorCodes
            .Where(c => c.UserID == userId && c.UsedUtc == null)
            .OrderByDescending(c => c.CodeID)
            .FirstOrDefaultAsync(ct);

        if (challenge is null) return TwoFactorResult.NoActiveCode;
        if (challenge.ExpiresUtc < DateTime.UtcNow) return TwoFactorResult.Expired;
        if (challenge.Attempts >= MaxAttempts) return TwoFactorResult.TooManyAttempts;

        challenge.Attempts++;

        if (!_hasher.Verify(submittedCode, challenge.CodeHash))
        {
            await _db.SaveChangesAsync(ct);
            return challenge.Attempts >= MaxAttempts
                ? TwoFactorResult.TooManyAttempts
                : TwoFactorResult.Invalid;
        }

        challenge.UsedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return TwoFactorResult.Success;
    }
}
