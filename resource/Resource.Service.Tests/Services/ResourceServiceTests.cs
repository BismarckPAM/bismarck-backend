using AutoMapper;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.DTOs;
using Resource.Service.Exceptions;
using Resource.Service.Mappings;
using Resource.Service.Models;
using Resource.Service.Services;
using Resource.Service.Validators;
using ResourceModel = Resource.Service.Models.Resource;

namespace Resource.Service.Tests.Services;

public class ResourceServiceTests
{
    private readonly IMapper _mapper;
    private readonly IValidator<CreateResourceRequest> _createValidator;
    private readonly IValidator<UpdateResourceRequest> _updateValidator;

    public ResourceServiceTests()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        _mapper = config.CreateMapper();
        _createValidator = new CreateResourceRequestValidator();
        _updateValidator = new UpdateResourceRequestValidator();
    }

    private static ResourceDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ResourceDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ResourceDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ReturnsCreatedResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var request = new CreateResourceRequest
        {
            Type = "Database",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Database", result.Type);
        Assert.Equal("DevOps", result.Owner);
        Assert.Equal("Production", result.Environment);
        Assert.Equal(ResourceCriticality.HIGH, result.Criticality);
        Assert.True(result.IsActive);

        var dbResource = await dbContext.Resources.FindAsync(result.Id);
        Assert.NotNull(dbResource);
        Assert.Equal("Database", dbResource.Type);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidRequest_ThrowsValidationException()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var invalidRequest = new CreateResourceRequest
        {
            Type = "", // Invalid: Empty
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(invalidRequest));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveResources()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var activeResource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "VM",
            Owner = "Team A",
            Environment = "Dev",
            Criticality = ResourceCriticality.LOW,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var inactiveResource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "Storage",
            Owner = "Team B",
            Environment = "Dev",
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Resources.AddRange(activeResource, inactiveResource);
        await dbContext.SaveChangesAsync();

        // Act
        var results = (await service.GetAllAsync()).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(activeResource.Id, results[0].Id);
        Assert.Equal("VM", results[0].Type);
    }

    [Fact]
    public async Task GetByIdAsync_WhenResourceExistsAndActive_ReturnsResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var resource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "Cluster",
            Owner = "Infra",
            Environment = "Prod",
            Criticality = ResourceCriticality.CRITICAL,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.GetByIdAsync(resource.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(resource.Id, result.Id);
        Assert.Equal("Cluster", result.Type);
    }

    [Fact]
    public async Task GetByIdAsync_WhenResourceDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdAsync_WhenResourceIsInactive_ThrowsNotFoundExceptionDueToQueryFilter()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var inactiveResource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "API",
            Owner = "Security",
            Environment = "Staging",
            Criticality = ResourceCriticality.HIGH,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(inactiveResource);
        await dbContext.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(inactiveResource.Id));
    }

    [Fact]
    public async Task UpdateAsync_WithValidRequest_UpdatesAndReturnsResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var resource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "AppService",
            Owner = "Old Owner",
            Environment = "QA",
            Criticality = ResourceCriticality.LOW,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        var updateRequest = new UpdateResourceRequest
        {
            Type = "AppServiceUpdated",
            Owner = "New Owner",
            Environment = "Prod",
            Criticality = ResourceCriticality.CRITICAL,
            IsActive = true
        };

        // Act
        var result = await service.UpdateAsync(resource.Id, updateRequest);

        // Assert
        Assert.Equal("AppServiceUpdated", result.Type);
        Assert.Equal("New Owner", result.Owner);
        Assert.Equal("Prod", result.Environment);
        Assert.Equal(ResourceCriticality.CRITICAL, result.Criticality);

        var dbResource = await dbContext.Resources.FindAsync(resource.Id);
        Assert.NotNull(dbResource);
        Assert.Equal("AppServiceUpdated", dbResource.Type);
        Assert.Equal("New Owner", dbResource.Owner);
    }

    [Fact]
    public async Task UpdateAsync_WhenResourceDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var updateRequest = new UpdateResourceRequest
        {
            Type = "Service",
            Owner = "Owner",
            Environment = "Prod",
            Criticality = ResourceCriticality.HIGH,
            IsActive = true
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(Guid.NewGuid(), updateRequest));
    }

    [Fact]
    public async Task UpdateAsync_CanReactivateDecommissionedResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var inactiveResource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "Server",
            Owner = "Ops",
            Environment = "Prod",
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(inactiveResource);
        await dbContext.SaveChangesAsync();

        var reactivateRequest = new UpdateResourceRequest
        {
            Type = "Server",
            Owner = "Ops",
            Environment = "Prod",
            Criticality = ResourceCriticality.HIGH,
            IsActive = true
        };

        // Act
        var result = await service.UpdateAsync(inactiveResource.Id, reactivateRequest);

        // Assert
        Assert.True(result.IsActive);
        Assert.Equal(ResourceCriticality.HIGH, result.Criticality);

        var activeInDb = await dbContext.Resources.FirstOrDefaultAsync(r => r.Id == inactiveResource.Id);
        Assert.NotNull(activeInDb);
        Assert.True(activeInDb.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_WhenResourceExists_DecommissionsResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var resource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "Router",
            Owner = "NetOps",
            Environment = "Prod",
            Criticality = ResourceCriticality.HIGH,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await service.DeleteAsync(resource.Id);

        // Assert
        Assert.False(result.IsActive);

        var activeQuery = await dbContext.Resources.FirstOrDefaultAsync(r => r.Id == resource.Id);
        Assert.Null(activeQuery);

        var rawResource = await dbContext.Resources.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == resource.Id);
        Assert.NotNull(rawResource);
        Assert.False(rawResource.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_WhenCalledTwice_IsIdempotentAndReturnsDecommissionedResource()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        var resource = new ResourceModel
        {
            Id = Guid.NewGuid(),
            Type = "Switch",
            Owner = "NetOps",
            Environment = "Prod",
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync();

        // Act
        var firstResult = await service.DeleteAsync(resource.Id);
        var secondResult = await service.DeleteAsync(resource.Id);

        // Assert
        Assert.False(firstResult.IsActive);
        Assert.False(secondResult.IsActive);
        Assert.Equal(firstResult.Id, secondResult.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenResourceDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange
        using var dbContext = CreateDbContext(Guid.NewGuid().ToString());
        var service = new ResourceService(dbContext, _mapper, _createValidator, _updateValidator);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
    }
}
