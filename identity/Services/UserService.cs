using AutoMapper;
using AutoMapper.QueryableExtensions;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.Service.Services;

public class UserService(IdentityDbContext dbContext, IMapper mapper, IPasswordHasher? passwordHasher = null) : IUserService
{
    private readonly IPasswordHasher passwordHasher = passwordHasher ?? new PasswordHasher();

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(request.RoleId, request.DepartmentId, cancellationToken);
        await EnsureEmailIsAvailableAsync(request.Email, null, cancellationToken);

        var user = mapper.Map<User>(request);
        user.Id = Guid.NewGuid();
        user.Email = request.Email.Trim().ToLowerInvariant();
        user.PasswordHash = passwordHasher.Hash(request.Password);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await ProjectUser(dbContext.Users.Where(item => item.Id == user.Id), cancellationToken);
    }

    public async Task<IReadOnlyList<UserResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .ProjectTo<UserResponse>(mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await ProjectUser(dbContext.Users.Where(user => user.Id == id), cancellationToken);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException($"User '{id}' was not found.");

        await ValidateReferencesAsync(request.RoleId, request.DepartmentId, cancellationToken);
        await EnsureEmailIsAvailableAsync(request.Email, id, cancellationToken);

        mapper.Map(request, user);
        user.Email = request.Email.Trim().ToLowerInvariant();
        await dbContext.SaveChangesAsync(cancellationToken);

        return await ProjectUser(dbContext.Users.IgnoreQueryFilters().Where(item => item.Id == id), cancellationToken);
    }

    public async Task<UserResponse> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException($"User '{id}' was not found.");

        user.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ProjectUser(dbContext.Users.IgnoreQueryFilters().Where(item => item.Id == id), cancellationToken);
    }

    private async Task ValidateReferencesAsync(Guid roleId, Guid departmentId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Roles.AnyAsync(role => role.Id == roleId, cancellationToken))
            throw new NotFoundException($"Role '{roleId}' was not found.");

        if (!await dbContext.Departments.AnyAsync(department => department.Id == departmentId, cancellationToken))
            throw new NotFoundException($"Department '{departmentId}' was not found.");
    }

    private async Task EnsureEmailIsAvailableAsync(string email, Guid? excludedUserId, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var query = dbContext.Users.IgnoreQueryFilters().Where(user => user.Email == normalizedEmail);
        if (excludedUserId.HasValue)
            query = query.Where(user => user.Id != excludedUserId.Value);

        if (await query.AnyAsync(cancellationToken))
            throw new DuplicateEmailException(email);
    }

    private async Task<UserResponse> ProjectUser(IQueryable<User> query, CancellationToken cancellationToken)
    {
        var user = await query.AsNoTracking()
            .ProjectTo<UserResponse>(mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
        return user ?? throw new NotFoundException("User was not found.");
    }
}
