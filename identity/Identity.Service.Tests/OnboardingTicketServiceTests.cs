using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.Service.Tests;

public sealed class OnboardingTicketServiceTests
{
    [Fact]
    public async Task CreateAsync_NormalizesInputAndReturnsPendingTicket()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(new CreateOnboardingTicketRequest
        {
            FullName = "  Ada Lovelace ",
            Email = " ADA@EXAMPLE.COM ",
            Department = " Engineering ",
            RequestedRole = " Admin ",
            Justification = " Need access "
        });

        var ticket = await context.OnboardingTickets.SingleAsync();
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Equal(OnboardingTicket.StatusPending, result.Status);
        Assert.Equal("ada@example.com", ticket.Email);
        Assert.Equal("Ada Lovelace", ticket.FullName);
        Assert.Equal("Need access", ticket.Justification);
    }

    [Fact]
    public async Task CreateAsync_RejectsExistingUserAndPendingTicket()
    {
        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(), FullName = "Existing", Email = "existing@example.com",
            PasswordHash = "hash", Role = new Role { Name = "User" },
            Department = new Department { Name = "General" }
        });
        context.OnboardingTickets.Add(new OnboardingTicket
        {
            Id = Guid.NewGuid(), FullName = "Pending", Email = "pending@example.com",
            Department = "General", RequestedRole = "User", Justification = "Reason"
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.CreateAsync(Request("existing@example.com")));
        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.CreateAsync(Request("pending@example.com")));
    }

    [Fact]
    public async Task RejectAsync_TrimsReasonAndSendsRejectionEmail()
    {
        await using var context = CreateContext();
        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(), FullName = "Applicant", Email = "applicant@example.com",
            Department = "General", RequestedRole = "User", Justification = "Reason"
        };
        context.OnboardingTickets.Add(ticket);
        await context.SaveChangesAsync();
        var email = new RecordingEmailSender();
        var service = CreateService(context, email);

        var result = await service.RejectAsync(ticket.Id, "admin", "  Not approved  ");

        Assert.Equal(OnboardingTicket.StatusRejected, result.Status);
        Assert.Equal("Not approved", result.RejectionReason);
        Assert.Equal(("applicant@example.com", "Applicant", "Not approved"), email.Rejection);
    }

    [Fact]
    public async Task ApproveAsync_ProvisionsUserAndSendsActivationEmail()
    {
        await using var context = CreateContext();
        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(), FullName = "Applicant", Email = "applicant@example.com",
            Department = "Engineering", RequestedRole = "Admin", Justification = "Reason"
        };
        context.OnboardingTickets.Add(ticket);
        await context.SaveChangesAsync();
        var email = new RecordingEmailSender();
        var service = CreateService(context, email);

        var result = await service.ApproveAsync(ticket.Id, "admin");
        var user = await context.Users.SingleAsync();

        Assert.Equal(OnboardingTicket.StatusApproved, result.Status);
        Assert.Equal(user.Id, result.ProvisionedUserId);
        Assert.Equal("applicant@example.com", user.Email);
        Assert.Equal("Engineering", (await context.Departments.FindAsync(user.DepartmentId))!.Name);
        Assert.Equal("Admin", (await context.Roles.FindAsync(user.RoleId))!.Name);
        Assert.NotNull(email.Activation);
        Assert.NotEmpty(email.Activation.Value.Password);
    }

    private static CreateOnboardingTicketRequest Request(string email) => new()
    {
        FullName = "Applicant", Email = email, Department = "General",
        RequestedRole = "User", Justification = "Reason"
    };

    private static IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OnboardingTicketService CreateService(
        IdentityDbContext context,
        IEmailSender? emailSender = null) =>
        new(context, new DeterministicHasher(), emailSender ?? new RecordingEmailSender(),
            NullLogger<OnboardingTicketService>.Instance);

    private sealed class DeterministicHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";
        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public (string Email, string Name, string Password)? Activation { get; private set; }
        public (string Email, string Name, string? Reason)? Rejection { get; private set; }

        public Task SendActivationEmailAsync(string toEmail, string fullName, string temporaryPassword,
            CancellationToken cancellationToken = default)
        {
            Activation = (toEmail, fullName, temporaryPassword);
            return Task.CompletedTask;
        }

        public Task SendRejectionEmailAsync(string toEmail, string fullName, string? reason,
            CancellationToken cancellationToken = default)
        {
            Rejection = (toEmail, fullName, reason);
            return Task.CompletedTask;
        }
    }
}
