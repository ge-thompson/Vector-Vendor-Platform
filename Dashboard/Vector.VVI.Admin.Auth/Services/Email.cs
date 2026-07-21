using Microsoft.Extensions.Logging;

namespace Vector.VVI.Admin.Auth.Services;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// Dev stub per the SMTP-deferred decision: does not send mail, just logs the
/// message (including any 2FA code) so IT can complete flows before real SMTP
/// credentials are wired in. Swap for an SmtpEmailSender when creds arrive.
/// </summary>
public sealed class DevStubEmailSender : IEmailSender
{
    private readonly ILogger<DevStubEmailSender> _log;

    public DevStubEmailSender(ILogger<DevStubEmailSender> log) => _log = log;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        _log.LogWarning("[DEV EMAIL STUB] To: {To} | Subject: {Subject}\n{Body}",
            toEmail, subject, body);
        return Task.CompletedTask;
    }
}
