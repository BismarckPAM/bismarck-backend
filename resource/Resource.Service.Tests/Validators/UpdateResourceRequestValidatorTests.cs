using FluentValidation.TestHelper;
using Resource.Service.DTOs;
using Resource.Service.Models;
using Resource.Service.Validators;

namespace Resource.Service.Tests.Validators;

public class UpdateResourceRequestValidatorTests
{
    private readonly UpdateResourceRequestValidator _validator = new();

    [Fact]
    public void Validate_WhenAllFieldsAreValid_PassesValidation()
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "Database",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.CRITICAL,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WhenTypeIsEmptyOrWhitespace_FailsValidation(string? type)
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = type!,
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.LOW,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WhenOwnerIsEmptyOrWhitespace_FailsValidation(string? owner)
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "VirtualMachine",
            Owner = owner!,
            Environment = "Production",
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Owner);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WhenEnvironmentIsEmptyOrWhitespace_FailsValidation(string? env)
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = env!,
            Criticality = ResourceCriticality.MEDIUM,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Environment);
    }

    [Fact]
    public void Validate_WhenCriticalityIsNull_FailsWithFriendlyMessage()
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = null,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Criticality)
            .WithErrorMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.");
    }

    [Fact]
    public void Validate_WhenCriticalityIsInvalidEnumValue_FailsWithFriendlyMessage()
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = (ResourceCriticality)999,
            IsActive = true
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Criticality)
            .WithErrorMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.");
    }

    [Fact]
    public void Validate_WhenIsActiveIsNull_FailsValidation()
    {
        // Arrange
        var request = new UpdateResourceRequest
        {
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH,
            IsActive = null
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.IsActive);
    }
}
