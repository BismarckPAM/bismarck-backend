using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Identity.Service.Tests;

public sealed class OnboardingTicketServiceTests
{
    [Fact]
    public async Task CreateAsync_NormalizesAndPersistsPendingTicket()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(new CreateOnboardingTicketRequest
        {
            FullName = " New User ",
            Email = " USER@EXAMPLE.COM ",
            Department = " Engineering ",
            RequestedRole = " Developer ",
            Justification = " Need access "
        });

        Assert.Equal(OnboardingTicket.StatusPending, result.Status);
        var ticket = await context.OnboardingTickets.SingleAsync();
        Assert.Equal("user@example.com", ticket.Email);
        Assert.Equal("New User", ticket.FullName);
    }

    [Fact]
    public async Task CreateAsync_RejectsExistingUserAndPendingTicket()
    {
        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(), Email = "existing@example.com", FullName = "Existing",
            PasswordHash = "hash", IsActive = true
        });
        context.OnboardingTickets.Add(new OnboardingTicket
        {
            Id = Guid.NewGuid(), Email = "pending@example.com", FullName = "Pending"
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.CreateAsync(Request("existing@example.com")));
        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.CreateAsync(Request("pending@example.com")));
    }

    [Fact]
    public async Task ApproveAsync_ProvisionsUserAndSendsActivationEmail()
    {
        await using var context = CreateContext();
        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(), FullName = "Applicant", Email = "applicant@example.com",
            Department = "Engineering", RequestedRole = "Developer", Justification = "Work"
        };
        context.OnboardingTickets.Add(ticket);
        await context.SaveChangesAsync();
        var email = new Mock<IEmailSender>();
        var service = CreateService(context, email: email.Object);

        var result = await service.ApproveAsync(ticket.Id, "admin");

        Assert.Equal(OnboardingTicket.StatusApproved, result.Status);
        Assert.NotNull(result.ProvisionedUserId);
        Assert.Single(context.Users);
        email.Verify(sender => sender.SendActivationEmailAsync(
            "applicant@example.com", "Applicant", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_StoresReasonAndSendsRejectionEmail()
    {
        await using var context = CreateContext();
        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(), FullName = "Applicant", Email = "applicant@example.com"
        };
        context.OnboardingTickets.Add(ticket);
        await context.SaveChangesAsync();
        var email = new Mock<IEmailSender>();
        var service = CreateService(context, email: email.Object);

        var result = await service.RejectAsync(ticket.Id, "admin", " Not approved ");

        Assert.Equal(OnboardingTicket.StatusRejected, result.Status);
        Assert.Equal("Not approved", result.RejectionReason);
        email.Verify(sender => sender.SendRejectionEmailAsync(
            "applicant@example.com", "Applicant", "Not approved", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_RejectsMissingAndAlreadyReviewedTickets()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveAsync(Guid.NewGuid(), "admin"));
        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(), Email = "done@example.com", Status = OnboardingTicket.StatusRejected
        };
        context.OnboardingTickets.Add(ticket);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(ticket.Id, "admin"));
    }

    private static CreateOnboardingTicketRequest Request(string email) => new()
    {
        FullName = "Applicant", Email = email, Department = "Engineering",
        RequestedRole = "Developer", Justification = "Work"
    };

    private static OnboardingTicketService CreateService(
        IdentityDbContext context,
        IEmailSender? email = null) =>
        new(
            context,
            PasswordHasher(),
            email ?? new Mock<IEmailSender>().Object,
            NullLogger<OnboardingTicketService>.Instance);

    private static IPasswordHasher PasswordHasher()
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.Hash(It.IsAny<string>())).Returns("temporary-hash");
        return hasher.Object;
    }

    private static IdentityDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
