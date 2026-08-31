using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resource.Service.DTOs;
using Resource.Service.Exceptions;
using Resource.Service.Services;

namespace Resource.Service.Controllers;

[Authorize]
[ApiController]
[Route("api/resources")]
public class ResourcesController(IResourceService resourceService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResourceResponse>> Create(CreateResourceRequest request)
    {
        var resource = await resourceService.CreateAsync(request);

        return CreatedAtAction(nameof(GetById), new { id = resource.Id }, resource);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ResourceResponse>>> GetAll()
    {
        var resources = await resourceService.GetAllAsync();

        return Ok(resources);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResourceResponse>> GetById(Guid id)
    {
        var resource = await resourceService.GetByIdAsync(id);

        return Ok(resource);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ResourceResponse>> Update(Guid id, UpdateResourceRequest request)
    {
        var resource = await resourceService.UpdateAsync(id, request);

        return Ok(resource);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ResourceResponse>> Delete(Guid id)
    {
        var resource = await resourceService.DeleteAsync(id);

        return Ok(resource);
    }
}
