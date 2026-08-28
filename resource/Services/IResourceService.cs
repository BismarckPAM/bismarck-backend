using Resource.Service.DTOs;

namespace Resource.Service.Services;

public interface IResourceService
{
    Task<ResourceResponse> CreateAsync(CreateResourceRequest request);
    Task<IEnumerable<ResourceResponse>> GetAllAsync();
    Task<ResourceResponse> GetByIdAsync(Guid id);
}
