using Resource.Service.DTOs;

namespace Resource.Service.Services;

public interface IResourceService
{
    Task<ResourceResponse> CreateAsync(CreateResourceRequest request);
    Task<IEnumerable<ResourceResponse>> GetAllAsync();
    Task<ResourceResponse> GetByIdAsync(Guid id);
    Task<ResourceResponse> UpdateAsync(Guid id, UpdateResourceRequest request);
    Task<ResourceResponse> DeleteAsync(Guid id);
}
