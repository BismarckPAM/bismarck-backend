using Identity.Service.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.Service.Tests;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendActivationEmailAsync_WithoutSmtpConfiguration_Completes()
    {
        var sender = new SmtpEmailSender(
            new ConfigurationBuilder().Build(),
            NullLogger<SmtpEmailSender>.Instance);

        await sender.SendActivationEmailAsync("user@example.com", "User", "temporary-password");
        await sender.SendRejectionEmailAsync("user@example.com", "User", null);
        await sender.SendRejectionEmailAsync("user@example.com", "User", "Reason");
    }
}
