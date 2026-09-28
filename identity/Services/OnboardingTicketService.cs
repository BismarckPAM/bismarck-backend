using System.Security.Cryptography;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.Service.Services;

public interface IOnboardingTicketService
{
    Task<CreateOnboardingTicketResponse> CreateAsync(
        CreateOnboardingTicketRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OnboardingTicketResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<OnboardingTicketResponse> ApproveAsync(
        Guid ticketId, string reviewedBy, CancellationToken cancellationToken = default);

    Task<OnboardingTicketResponse> RejectAsync(
        Guid ticketId, string reviewedBy, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the onboarding lifecycle: an unauthenticated visitor submits a ticket,
/// an administrator approves it (which provisions a <see cref="User"/> and sends
/// an activation email) or rejects it (which notifies the applicant).
/// </summary>
public sealed class OnboardingTicketService(
    IdentityDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    ILogger<OnboardingTicketService> logger) : IOnboardingTicketService
{
    private const string AllowedPasswordChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
    private const int TemporaryPasswordLength = 16;

    public async Task<CreateOnboardingTicketResponse> CreateAsync(
        CreateOnboardingTicketRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Never issue a ticket for an account that already exists.
        var userExists = await dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email, cancellationToken);
        if (userExists)
            throw new DuplicateEmailException(email);

        var alreadyPending = await dbContext.OnboardingTickets
            .AnyAsync(ticket => ticket.Email == email
                && ticket.Status == OnboardingTicket.StatusPending, cancellationToken);
        if (alreadyPending)
            throw new DuplicateEmailException(email);

        var ticket = new OnboardingTicket
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Department = request.Department.Trim(),
            RequestedRole = request.RequestedRole.Trim(),
            Justification = request.Justification.Trim(),
            Status = OnboardingTicket.StatusPending,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OnboardingTickets.Add(ticket);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Onboarding ticket {TicketId} submitted for {Email}.", ticket.Id, email);

        return new CreateOnboardingTicketResponse
        {
            TicketId = ticket.Id,
            Status = ticket.Status,
            Message = "Your access request has been submitted and is awaiting administrator review."
        };
    }

    public async Task<IReadOnlyList<OnboardingTicketResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.OnboardingTickets
            .AsNoTracking()
            .OrderByDescending(ticket => ticket.CreatedAt)
            .Select(ticket => ToResponse(ticket))
            .ToListAsync(cancellationToken);
    }

    public async Task<OnboardingTicketResponse> ApproveAsync(
        Guid ticketId, string reviewedBy, CancellationToken cancellationToken = default)
    {
        var ticket = await dbContext.OnboardingTickets
            .FirstOrDefaultAsync(item => item.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException($"Onboarding ticket '{ticketId}' was not found.");

        if (!string.Equals(ticket.Status, OnboardingTicket.StatusPending, StringComparison.Ordinal))
            throw new InvalidOperationException($"Ticket '{ticketId}' has already been {ticket.Status}.");

        var role = await ResolveRoleAsync(ticket.RequestedRole, cancellationToken);
        var department = await ResolveDepartmentAsync(ticket.Department, cancellationToken);

        var (password, passwordHash) = GenerateTemporaryPassword();

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = ticket.FullName,
            Email = ticket.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            RoleId = role.Id,
            DepartmentId = department.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user);

        ticket.Status = OnboardingTicket.StatusApproved;
        ticket.ReviewedAt = DateTime.UtcNow;
        ticket.ReviewedBy = reviewedBy;
        ticket.RejectionReason = null;
        ticket.ProvisionedUserId = user.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendActivationEmailAsync(user.Email, user.FullName, password, cancellationToken);

        logger.LogInformation(
            "Onboarding ticket {TicketId} approved by {Reviewer}; user {UserId} provisioned.",
            ticket.Id, reviewedBy, user.Id);

        return ToResponse(ticket);
    }

    public async Task<OnboardingTicketResponse> RejectAsync(
        Guid ticketId, string reviewedBy, string? reason, CancellationToken cancellationToken = default)
    {
        var ticket = await dbContext.OnboardingTickets
            .FirstOrDefaultAsync(item => item.Id == ticketId, cancellationToken)
            ?? throw new NotFoundException($"Onboarding ticket '{ticketId}' was not found.");

        if (!string.Equals(ticket.Status, OnboardingTicket.StatusPending, StringComparison.Ordinal))
            throw new InvalidOperationException($"Ticket '{ticketId}' has already been {ticket.Status}.");

        ticket.Status = OnboardingTicket.StatusRejected;
        ticket.ReviewedAt = DateTime.UtcNow;
        ticket.ReviewedBy = reviewedBy;
        ticket.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendRejectionEmailAsync(
            ticket.Email, ticket.FullName, ticket.RejectionReason, cancellationToken);

        logger.LogInformation("Onboarding ticket {TicketId} rejected by {Reviewer}.", ticket.Id, reviewedBy);

        return ToResponse(ticket);
    }

    private async Task<Role> ResolveRoleAsync(string requestedRole, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(requestedRole) ? "User" : requestedRole.Trim();
        var role = await dbContext.Roles
            .FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);

        if (role is null)
        {
            role = new Role { Id = Guid.NewGuid(), Name = name };
            dbContext.Roles.Add(role);
        }

        return role;
    }

    private async Task<Department> ResolveDepartmentAsync(string requestedDepartment, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(requestedDepartment) ? "General" : requestedDepartment.Trim();
        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Name.ToLower() == name.ToLower(), cancellationToken);

        if (department is null)
        {
            department = new Department { Id = Guid.NewGuid(), Name = name };
            dbContext.Departments.Add(department);
        }

        return department;
    }

    private (string Password, string Hash) GenerateTemporaryPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(TemporaryPasswordLength);
        var chars = new char[TemporaryPasswordLength];
        for (var i = 0; i < TemporaryPasswordLength; i++)
            chars[i] = AllowedPasswordChars[bytes[i] % AllowedPasswordChars.Length];

        var password = new string(chars);
        return (password, passwordHasher.Hash(password));
    }

    private static OnboardingTicketResponse ToResponse(OnboardingTicket ticket) => new()
    {
        Id = ticket.Id,
        FullName = ticket.FullName,
        Email = ticket.Email,
        Department = ticket.Department,
        RequestedRole = ticket.RequestedRole,
        Justification = ticket.Justification,
        Status = ticket.Status,
        CreatedAt = ticket.CreatedAt,
        ReviewedAt = ticket.ReviewedAt,
        ReviewedBy = ticket.ReviewedBy,
        RejectionReason = ticket.RejectionReason,
        ProvisionedUserId = ticket.ProvisionedUserId
    };
}