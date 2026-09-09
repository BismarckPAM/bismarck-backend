using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Resource.Service.DTOs;
using Resource.Service.IntegrationTests.Fixtures;
using Resource.Service.Models;
using Xunit;

namespace Resource.Service.IntegrationTests.Controllers;

public class ResourcesControllerIntegrationTests : IClassFixture<ResourceApiFactory>
{
    private readonly HttpClient _client;
    private readonly ResourceApiFactory _factory;
    private readonly string _validJwtToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ResourcesControllerIntegrationTests(ResourceApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _validJwtToken = ResourceApiFactory.GenerateValidJwtToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _validJwtToken);
    }

    [Fact]
    public async Task GetAll_WithoutAuthorizationHeader_Returns401Unauthorized()
    {
        var unauthenticatedClient = _factory.CreateClient();

        var response = await unauthenticatedClient.GetAsync("/api/resources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithMalformedBearerToken_Returns401Unauthorized()
    {
        var unauthenticatedClient = _factory.CreateClient();
        unauthenticatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.garbage.token");

        var response = await unauthenticatedClient.GetAsync("/api/resources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithValidBearerToken_Returns200OK()
    {
        var response = await _client.GetAsync("/api/resources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_ReturnsCorsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/resources");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal("http://localhost:5173", origins.Single());
    }

    [Fact]
    public async Task FullHttpLifecycle_AndSoftDelete_Verification()
    {
        // 1. POST: Create a resource
        var createRequest = new CreateResourceRequest
        {
            Type = "ComputeInstance",
            Owner = "PlatformTeam",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
        };

        var postResponse = await _client.PostAsJsonAsync("/api/resources", createRequest, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var createdResource = await postResponse.Content.ReadFromJsonAsync<ResourceResponse>(JsonOptions);
        Assert.NotNull(createdResource);
        Assert.NotEqual(Guid.Empty, createdResource.Id);
        Assert.Equal("ComputeInstance", createdResource.Type);
        Assert.Equal("PlatformTeam", createdResource.Owner);
        Assert.Equal("Production", createdResource.Environment);
        Assert.Equal(ResourceCriticality.HIGH, createdResource.Criticality);
        Assert.True(createdResource.IsActive);

        var resourceId = createdResource.Id;

        // 2. GET (all): Confirm it's listed
        var getAllResponse = await _client.GetAsync("/api/resources");
        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

        var allResources = await getAllResponse.Content.ReadFromJsonAsync<List<ResourceResponse>>(JsonOptions);
        Assert.NotNull(allResources);
        Assert.Contains(allResources, r => r.Id == resourceId);

        // 3. GET (by id): Confirm it matches
        var getByIdResponse = await _client.GetAsync($"/api/resources/{resourceId}");
        Assert.Equal(HttpStatusCode.OK, getByIdResponse.StatusCode);

        var fetchedResource = await getByIdResponse.Content.ReadFromJsonAsync<ResourceResponse>(JsonOptions);
        Assert.NotNull(fetchedResource);
        Assert.Equal(resourceId, fetchedResource.Id);
        Assert.Equal("ComputeInstance", fetchedResource.Type);

        // 4. PUT: Update the resource
        var updateRequest = new UpdateResourceRequest
        {
            Type = "ComputeInstanceUpdated",
            Owner = "PlatformSec",
            Environment = "Staging",
            Criticality = ResourceCriticality.CRITICAL,
            IsActive = true
        };

        var putResponse = await _client.PutAsJsonAsync($"/api/resources/{resourceId}", updateRequest, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var updatedResource = await putResponse.Content.ReadFromJsonAsync<ResourceResponse>(JsonOptions);
        Assert.NotNull(updatedResource);
        Assert.Equal("ComputeInstanceUpdated", updatedResource.Type);
        Assert.Equal("PlatformSec", updatedResource.Owner);
        Assert.Equal("Staging", updatedResource.Environment);
        Assert.Equal(ResourceCriticality.CRITICAL, updatedResource.Criticality);

        // 5. DELETE: Soft-delete the resource
        var deleteResponse = await _client.DeleteAsync($"/api/resources/{resourceId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var deletedResource = await deleteResponse.Content.ReadFromJsonAsync<ResourceResponse>(JsonOptions);
        Assert.NotNull(deletedResource);
        Assert.False(deletedResource.IsActive);

        // 6. GET (all): Confirm it's gone from the active list
        var getAllAfterDeleteResponse = await _client.GetAsync("/api/resources");
        Assert.Equal(HttpStatusCode.OK, getAllAfterDeleteResponse.StatusCode);

        var activeResourcesAfterDelete = await getAllAfterDeleteResponse.Content.ReadFromJsonAsync<List<ResourceResponse>>(JsonOptions);
        Assert.NotNull(activeResourcesAfterDelete);
        Assert.DoesNotContain(activeResourcesAfterDelete, r => r.Id == resourceId);

        // 7. GET (by id): Confirm inactive returns 404 (due to global filter)
        var getInactiveByIdResponse = await _client.GetAsync($"/api/resources/{resourceId}");
        Assert.Equal(HttpStatusCode.NotFound, getInactiveByIdResponse.StatusCode);

        // 8. Direct DB Context check with IgnoreQueryFilters: Prove entity is still in DB (soft-deleted)
        using var dbContext = _factory.CreateDbContext();
        var rawDbEntity = await dbContext.Resources
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == resourceId);

        Assert.NotNull(rawDbEntity);
        Assert.False(rawDbEntity.IsActive);
        Assert.Equal("ComputeInstanceUpdated", rawDbEntity.Type);
    }

    [Fact]
    public async Task GetById_WhenUnknownId_Returns404NotFound()
    {
        var unknownId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/resources/{unknownId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenUnknownId_Returns404NotFound()
    {
        var unknownId = Guid.NewGuid();
        var updateRequest = new UpdateResourceRequest
        {
            Type = "Database",
            Owner = "Owner",
            Environment = "Prod",
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = true
        };

        var response = await _client.PutAsJsonAsync($"/api/resources/{unknownId}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenUnknownId_Returns404NotFound()
    {
        var unknownId = Guid.NewGuid();
        var response = await _client.DeleteAsync($"/api/resources/{unknownId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidCriticalityString_Returns400WithStructuredErrorBody()
    {
        var rawJson = """
        {
            "type": "Server",
            "owner": "Team",
            "environment": "Prod",
            "criticality": "URGENT"
        }
        """;

        var content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/resources", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>(JsonOptions);
        Assert.NotNull(errorBody);
        Assert.NotEmpty(errorBody.Errors);
        Assert.Contains(errorBody.Errors, e =>
            e.PropertyName.Equals("criticality", StringComparison.OrdinalIgnoreCase) &&
            e.ErrorMessage.Contains("LOW, MEDIUM, HIGH, CRITICAL"));
    }

    [Fact]
    public async Task Create_WithMissingRequiredFields_Returns400WithStructuredErrorBody()
    {
        var invalidRequest = new CreateResourceRequest
        {
            Type = "",
            Owner = "",
            Environment = "",
            Criticality = ResourceCriticality.HIGH
        };

        var response = await _client.PostAsJsonAsync("/api/resources", invalidRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>(JsonOptions);
        Assert.NotNull(errorBody);
        Assert.True(errorBody.Errors.Count >= 3);
        Assert.Contains(errorBody.Errors, e => e.PropertyName.Equals("Type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errorBody.Errors, e => e.PropertyName.Equals("Owner", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errorBody.Errors, e => e.PropertyName.Equals("Environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_WithNullIsActive_Returns400WithStructuredErrorBody()
    {
        // First create a resource to update
        var createRequest = new CreateResourceRequest
        {
            Type = "StorageAccount",
            Owner = "StorageTeam",
            Environment = "Dev",
            Criticality = ResourceCriticality.LOW
        };

        var createResponse = await _client.PostAsJsonAsync("/api/resources", createRequest, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ResourceResponse>(JsonOptions);
        Assert.NotNull(created);

        var invalidUpdateRequest = new UpdateResourceRequest
        {
            Type = "StorageAccount",
            Owner = "StorageTeam",
            Environment = "Dev",
            Criticality = ResourceCriticality.LOW,
            IsActive = null
        };

        var putResponse = await _client.PutAsJsonAsync($"/api/resources/{created.Id}", invalidUpdateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);

        var errorBody = await putResponse.Content.ReadFromJsonAsync<ValidationErrorResponse>(JsonOptions);
        Assert.NotNull(errorBody);
        Assert.Contains(errorBody.Errors, e => e.PropertyName.Equals("IsActive", StringComparison.OrdinalIgnoreCase));
    }

    public record ValidationErrorResponse(List<ValidationErrorItem> Errors);
    public record ValidationErrorItem(string PropertyName, string ErrorMessage);
}
