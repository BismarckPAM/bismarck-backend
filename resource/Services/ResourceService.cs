using AutoMapper;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.DTOs;
using Resource.Service.Exceptions;
using Resource.Service.Models;
using ResourceModel = Resource.Service.Models.Resource;
using Messaging;

namespace Resource.Service.Services;

public class ResourceService(
    ResourceDbContext dbContext,
    IMapper mapper,
    IValidator<CreateResourceRequest> createValidator,
    IValidator<UpdateResourceRequest> updateValidator,
    IDomainEventPublisher? domainEventPublisher = null) : IResourceService
{
    private readonly IDomainEventPublisher eventPublisher = domainEventPublisher ?? new NullDomainEventPublisher();

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

        var response = mapper.Map<ResourceResponse>(resource);
        await eventPublisher.PublishAsync(
            "resource-events",
            new SecurityEvent<object>(
            Guid.NewGuid(),
            "resource-created",
            DateTimeOffset.UtcNow,
            response.Owner,
            response.Id.ToString(),
            "RESOURCE_CREATE",
            "SUCCESS",
            new
            {
                response.Type,
                response.Owner,
                response.Environment,
                response.Criticality,
                response.IsActive,
                response.CreatedAt
            }));

        return response;
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

        var response = mapper.Map<ResourceResponse>(resource);
        await eventPublisher.PublishAsync(
            "resource-events",
            new SecurityEvent<object>(
            Guid.NewGuid(),
            "resource-updated",
            DateTimeOffset.UtcNow,
            response.Owner,
            response.Id.ToString(),
            "RESOURCE_UPDATE",
            "SUCCESS",
            new
            {
                response.Type,
                response.Owner,
                response.Environment,
                response.Criticality,
                response.IsActive,
                response.CreatedAt
            }));

        return response;
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
