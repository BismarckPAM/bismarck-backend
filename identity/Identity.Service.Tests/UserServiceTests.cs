using AutoMapper;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Mappings;
using Identity.Service.Models;
using Identity.Service.Services;
using Identity.Service.Validators;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Identity.Service.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task CreateAndReadUser()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);

        var result = await service.CreateAsync(Request(role.Id, department.Id));

        Assert.Equal("person@example.com", result.Email);
        Assert.Equal("Admin", result.Role);
        Assert.Equal("Engineering", result.Department);
        Assert.Single(await service.GetAllAsync());
        Assert.Equal(result.Id, (await service.GetByIdAsync(result.Id)).Id);
    }

    [Fact]
    public async Task CreateRejectsMissingReferencesAndDuplicateEmail()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);
        var request = Request(role.Id, department.Id);
        await service.CreateAsync(request);

        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(Request(Guid.NewGuid(), department.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(Request(role.Id, Guid.NewGuid())));
        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateChangesReferencesStatusAndRejectsDuplicates()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var otherRole = new Role { Id = Guid.NewGuid(), Name = "Viewer" };
        var otherDepartment = new Department { Id = Guid.NewGuid(), Name = "Support" };
        context.Roles.Add(otherRole);
        context.Departments.Add(otherDepartment);
        var service = CreateService(context);
        var created = await service.CreateAsync(Request(role.Id, department.Id));
        var secondRequest = Request(role.Id, department.Id);
        secondRequest.Email = "second@example.com";
        await service.CreateAsync(secondRequest);

        var updated = await service.UpdateAsync(created.Id, new UpdateUserRequest
        {
            FullName = "Updated Person", Email = "updated@example.com", RoleId = otherRole.Id,
            DepartmentId = otherDepartment.Id, IsActive = false
        });

        Assert.False(updated.IsActive);
        Assert.Single(await service.GetAllAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(created.Id));
        await Assert.ThrowsAsync<DuplicateEmailException>(() => service.UpdateAsync(created.Id, new UpdateUserRequest
        {
            FullName = "Other", Email = "second@example.com", RoleId = role.Id,
            DepartmentId = department.Id, IsActive = true
        }));
        var reactivated = await service.UpdateAsync(created.Id, new UpdateUserRequest
        {
            FullName = "Reactivated", Email = "reactivated@example.com", RoleId = role.Id,
            DepartmentId = department.Id, IsActive = true
        });
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task DeleteSoftDeletesAndMissingIdsThrow()
    {
        await using var context = CreateContext();
        var (role, department) = SeedReferences(context);
        var service = CreateService(context);
        var created = await service.CreateAsync(Request(role.Id, department.Id));

        var deleted = await service.DeleteAsync(created.Id);

        Assert.False(deleted.IsActive);
        Assert.Empty(await service.GetAllAsync());
        Assert.False(context.Users.IgnoreQueryFilters().Single().IsActive);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ValidatorsRejectMissingRequiredFields()
    {
        var createResult = await new CreateUserRequestValidator().ValidateAsync(new CreateUserRequest());
        var updateResult = await new UpdateUserRequestValidator().ValidateAsync(new UpdateUserRequest());

        Assert.False(createResult.IsValid);
        Assert.False(updateResult.IsValid);
        Assert.Contains(createResult.Errors, error => error.PropertyName == nameof(CreateUserRequest.RoleId));
        Assert.Contains(updateResult.Errors, error => error.PropertyName == nameof(UpdateUserRequest.IsActive));
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

    private static UserService CreateService(IdentityDbContext context)
        => new(context, new MapperConfiguration(configuration => configuration.AddProfile<MappingProfile>()).CreateMapper());

    private static CreateUserRequest Request(Guid roleId, Guid departmentId)
        => new() { FullName = "Person", Email = "PERSON@example.com", RoleId = roleId, DepartmentId = departmentId };
}
