using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.DTOs;
using Resource.Service.Exceptions;
using Resource.Service.Models;
using ResourceModel = Resource.Service.Models.Resource;

namespace Resource.Service.Services;

public class ResourceService(ResourceDbContext dbContext, IMapper mapper) : IResourceService
{
    public async Task<ResourceResponse> CreateAsync(CreateResourceRequest request)
    {
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
