using AutoMapper;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.DTOs;
using Resource.Service.Exceptions;
using Resource.Service.Models;
using ResourceModel = Resource.Service.Models.Resource;

namespace Resource.Service.Services;

public class ResourceService(
    ResourceDbContext dbContext,
    IMapper mapper,
    IValidator<CreateResourceRequest> createValidator,
    IValidator<UpdateResourceRequest> updateValidator) : IResourceService
{
    public async Task<ResourceResponse> CreateAsync(CreateResourceRequest request)
    {
        if (request is null)
        {
            throw new ValidationException(new[] { new ValidationFailure("request", "Request body cannot be null.") });
        }

        await createValidator.ValidateAndThrowAsync(request);

        var resource = mapper.Map<ResourceModel>(request);

        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        return mapper.Map<ResourceResponse>(resource);
    }

    public async Task<IEnumerable<ResourceResponse>> GetAllAsync()
    {
        var resources = await dbContext.Resources
            .AsNoTracking()
            .ToListAsync();

        return mapper.Map<IEnumerable<ResourceResponse>>(resources);
    }

    public async Task<ResourceResponse> GetByIdAsync(Guid id)
    {
        var resource = await dbContext.Resources
            .AsNoTracking()
            .FirstOrDefaultAsync(resource => resource.Id == id);

        if (resource is null)
        {
            throw new NotFoundException($"Resource with id '{id}' was not found.");
        }

        return mapper.Map<ResourceResponse>(resource);
    }

    public async Task<ResourceResponse> UpdateAsync(Guid id, UpdateResourceRequest request)
    {
        if (request is null)
        {
            throw new ValidationException(new[] { new ValidationFailure("request", "Request body cannot be null.") });
        }

        await updateValidator.ValidateAndThrowAsync(request);

        var resource = await dbContext.Resources
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(resource => resource.Id == id);

        if (resource is null)
        {
            throw new NotFoundException($"Resource with id '{id}' was not found.");
        }

        mapper.Map(request, resource);
        await dbContext.SaveChangesAsync();

        return mapper.Map<ResourceResponse>(resource);
    }

    public async Task<ResourceResponse> DeleteAsync(Guid id)
    {
        var resource = await dbContext.Resources
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(resource => resource.Id == id);

        if (resource is null)
        {
            throw new NotFoundException($"Resource with id '{id}' was not found.");
        }

        if (resource.IsActive)
        {
            resource.IsActive = false;
            await dbContext.SaveChangesAsync();
        }

        return mapper.Map<ResourceResponse>(resource);
    }
}
