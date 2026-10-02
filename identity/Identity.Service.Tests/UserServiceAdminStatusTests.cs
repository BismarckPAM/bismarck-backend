using AutoMapper;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Mappings;
using Identity.Service.Models;
using Identity.Service.Services;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Identity.Service.Tests;

/// <summary>
/// BIS-405: Admin user management — the directory that includes DEACTIVATED
/// accounts, and the focused activate/deactivate operation.
/// </summary>
public class AdminUserManagementTests
{
    [Fact]
    public async Task AdminList_IncludesInactiveAccounts_WhileNormalListDoesNot()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);

        var active = await service.CreateAsync(Request(role.Id, department.Id, "active@example.com"));
        var inactive = await service.CreateAsync(Request(role.Id, department.Id, "gone@example.com"));
        await service.DeleteAsync(inactive.Id);

        // The default listing keeps the User.IsActive query filter, so existing
        // consumers (Access Check) never see a switched-off account.
        Assert.Single(await service.GetAllAsync());

        var all = await service.GetAllIncludingInactiveAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, item => item.Id == active.Id && item.IsActive);
        Assert.Contains(all, item => item.Id == inactive.Id && !item.IsActive);
    }

    [Fact]
    public async Task AdminList_IsSortedByFullName_AndNeverExposesSecrets()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);
        await service.CreateAsync(Request(role.Id, department.Id, "zoe@example.com", "Zoe Adams"));
        await service.CreateAsync(Request(role.Id, department.Id, "amy@example.com", "Amy Baker"));

        var all = await service.GetAllIncludingInactiveAsync();

        Assert.Equal(["Amy Baker", "Zoe Adams"], all.Select(item => item.FullName).ToArray());
        Assert.All(all, item =>
        {
            Assert.NotNull(item.Role);
            Assert.NotNull(item.Department);
        });
        // UserResponse carries no credential material of any kind.
        Assert.DoesNotContain(
            typeof(UserResponse).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateStatus_DeactivatesAndReactivates_PreservingEverythingElse()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(Request(role.Id, department.Id, "keep@example.com", "Keep Me"));

        var deactivated = await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = false });

        Assert.False(deactivated.IsActive);
        Assert.Equal(created.FullName, deactivated.FullName);
        Assert.Equal(created.Email, deactivated.Email);
        Assert.Equal(created.RoleId, deactivated.RoleId);
        Assert.Equal(created.Role, deactivated.Role);
        Assert.Equal(created.DepartmentId, deactivated.DepartmentId);
        Assert.Equal(created.Department, deactivated.Department);
        Assert.Equal(created.CreatedAt, deactivated.CreatedAt);

        var reactivated = await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = true });

        Assert.True(reactivated.IsActive);
        Assert.Equal(created.Email, reactivated.Email);
        // Once active again, the account is back in the normal listing.
        Assert.Single(await service.GetAllAsync());
    }

    [Fact]
    public async Task UpdateStatus_IsIdempotentForTheSameRequestedStatus()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(Request(role.Id, department.Id, "again@example.com"));

        await service.UpdateStatusAsync(created.Id, new UpdateUserStatusRequest { IsActive = false });
        // Re-applying the same status must not throw, so a retried UI request is safe.
        var second = await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = false });
        Assert.False(second.IsActive);
        Assert.Equal(created.Id, second.Id);

        await service.UpdateStatusAsync(created.Id, new UpdateUserStatusRequest { IsActive = true });
        var activeAgain = await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = true });
        Assert.True(activeAgain.IsActive);
        Assert.Equal(created.Email, activeAgain.Email);
    }

    [Fact]
    public async Task UpdateStatus_UnknownUserThrowsNotFound()
    {
        await using var context = CreateContext();
        SeedReferences(context);
        var service = CreateService(context);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateStatusAsync(Guid.NewGuid(), new UpdateUserStatusRequest { IsActive = false }));
    }

    [Fact]
    public async Task UpdateStatus_PublishesActivationAndDeactivationAuditEvents()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var publisher = new RecordingDomainEventPublisher();
        var service = CreateService(context, publisher);
        var created = await service.CreateAsync(Request(role.Id, department.Id, "audited@example.com"));

        await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = false }, "admin-1");
        await service.UpdateStatusAsync(
            created.Id, new UpdateUserStatusRequest { IsActive = true }, "admin-1");

        var deactivate = publisher.Events.Single(item => item.Action == "USER_DEACTIVATE");
        var activate = publisher.Events.Single(item => item.Action == "USER_ACTIVATE");

        // Same channel and schema as user-created / user-updated: no new Kafka contract.
        Assert.All(publisher.Events, item => Assert.Equal("identity-events", item.Topic));
        Assert.All(publisher.Events, item => Assert.Equal("SUCCESS", item.Outcome));
        Assert.All(publisher.Events, item => Assert.NotEqual(Guid.Empty, item.EventId));
        Assert.Equal("admin-1", deactivate.Actor);
        Assert.Equal(created.Id.ToString(), deactivate.Resource);
        Assert.NotNull(activate);
    }

    private static IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

    private static (Role Role, Department Department) SeedReferences(IdentityDbContext context)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var department = new Department { Id = Guid.NewGuid(), Name = "Engineering" };
        context.Roles.Add(role);
        context.Departments.Add(department);
        context.SaveChanges();
        return (role, department);
    }

    private static UserService CreateService(
        IdentityDbContext context,
        IDomainEventPublisher? publisher = null)
        => new(
            context,
            new MapperConfiguration(configuration => configuration.AddProfile<MappingProfile>()).CreateMapper(),
            publisher: publisher);

    private static CreateUserRequest Request(
        Guid roleId,
        Guid departmentId,
        string email,
        string? fullName = null)
        => new()
        {
            FullName = fullName ?? "Person",
            Email = email,
            RoleId = roleId,
            DepartmentId = departmentId
        };

    private sealed class RecordingDomainEventPublisher : IDomainEventPublisher
    {
        public List<RecordedEvent> Events { get; } = new();

        public Task PublishAsync<T>(
            string topic,
            SecurityEvent<T> message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new RecordedEvent(
                topic, message.EventId, message.Action, message.Outcome, message.Actor, message.Resource));
            return Task.CompletedTask;
        }

        public sealed record RecordedEvent(
            string Topic,
            Guid EventId,
            string Action,
            string Outcome,
            string Actor,
            string? Resource);
    }
}