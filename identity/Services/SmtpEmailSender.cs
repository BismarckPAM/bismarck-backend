using System.Net;
using System.Net.Mail;

namespace Identity.Service.Services;

/// <summary>Sends transactional onboarding emails.</summary>
public interface IEmailSender
{
    Task SendActivationEmailAsync(
        string toEmail, string fullName, string temporaryPassword, CancellationToken cancellationToken = default);

    Task SendRejectionEmailAsync(
        string toEmail, string fullName, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// SMTP-based email sender. When no SMTP host is configured the message content
/// is written to the log instead of being sent, so local development and tests
/// never require a mail server (and never throw).
/// </summary>
public sealed class SmtpEmailSender(
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly string? _host = configuration["Smtp:Host"];
    private readonly int _port = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 25;
    private readonly string _from = configuration["Smtp:From"] ?? "no-reply@bismarck.local";
    private readonly string? _username = configuration["Smtp:Username"];
    private readonly string? _password = configuration["Smtp:Password"];
    private readonly bool _useSsl = bool.TryParse(configuration["Smtp:UseSsl"], out var ssl) && ssl;

    public Task SendActivationEmailAsync(
        string toEmail, string fullName, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var body =
            $"""
             Hello {fullName},

             Your Bismarck Privileged Access Management account has been provisioned.

             Sign in with:
               Email:     {toEmail}
               Temporary password: {temporaryPassword}

             For your security, please change this password immediately after signing in.

             — Bismarck PAM
             """;
        return SendAsync(toEmail, "Your Bismarck PAM account is ready", body, cancellationToken);
    }

    public Task SendRejectionEmailAsync(
        string toEmail, string fullName, string? reason, CancellationToken cancellationToken = default)
    {
        var reasonLine = string.IsNullOrWhiteSpace(reason) ? string.Empty : $"\nReason: {reason}\n";
        var body =
            $"""
             Hello {fullName},

             We are unable to approve your Bismarck PAM access request at this time.
             {reasonLine}
             If you believe this is a mistake, please contact your administrator.

             — Bismarck PAM
             """;
        return SendAsync(toEmail, "Your Bismarck PAM access request", body, cancellationToken);
    }

    private async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_host))
        {
            logger.LogWarning(
                "SMTP is not configured; email to {To} with subject '{Subject}' was not sent. Body:\n{Body}",
                toEmail, subject, body);
            return;
        }

        try
        {
            using var message = new MailMessage(_from, toEmail, subject, body);
            using var client = new SmtpClient(_host, _port) { EnableSsl = _useSsl };
            if (!string.IsNullOrWhiteSpace(_username))
                client.Credentials = new NetworkCredential(_username, _password);

            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("Sent '{Subject}' email to {To}.", subject, toEmail);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Email delivery must not fail provisioning.
            logger.LogError(exception, "Failed to send '{Subject}' email to {To}.", subject, toEmail);
        }
    }
}