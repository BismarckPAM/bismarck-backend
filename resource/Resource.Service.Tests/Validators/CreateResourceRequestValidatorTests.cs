using FluentValidation.TestHelper;
using Resource.Service.DTOs;
using Resource.Service.Models;
using Resource.Service.Validators;

namespace Resource.Service.Tests.Validators;

public class CreateResourceRequestValidatorTests
{
    private readonly CreateResourceRequestValidator _validator = new();

    [Fact]
    public void Validate_WhenAllFieldsAreValid_PassesValidation()
    {
        // Arrange
        var request = new CreateResourceRequest
        {
            Name = "Production Database",
            Type = "Database",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
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
        var request = new CreateResourceRequest
        {
            Name = "Virtual Machine",
            Type = type!,
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.LOW
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
        var request = new CreateResourceRequest
        {
            Name = "Virtual Machine",
            Type = "VirtualMachine",
            Owner = owner!,
            Environment = "Production",
            Criticality = ResourceCriticality.MEDIUM
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
        var request = new CreateResourceRequest
        {
            Name = "Virtual Machine",
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = env!,
            Criticality = ResourceCriticality.MEDIUM
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
        var request = new CreateResourceRequest
        {
            Name = "Virtual Machine",
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = null
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
        var request = new CreateResourceRequest
        {
            Name = "Virtual Machine",
            Type = "VirtualMachine",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = (ResourceCriticality)999
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(r => r.Criticality)
            .WithErrorMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WhenNameIsMissingOrWhitespace_FailsValidation(string? name)
    {
        var request = new CreateResourceRequest
        {
            Name = name!,
            Type = "Database",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(r => r.Name);
    }

    [Fact]
    public void Validate_WhenNameExceedsMaximumLength_FailsValidation()
    {
        var request = new CreateResourceRequest
        {
            Name = new string('N', 201),
            Type = "Database",
            Owner = "DevOps",
            Environment = "Production",
            Criticality = ResourceCriticality.HIGH
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(r => r.Name);
    }
}
