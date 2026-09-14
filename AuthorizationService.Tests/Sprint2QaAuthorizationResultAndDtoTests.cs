using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Sprint 2 QA unit tests for result factories and DTO validation contracts.
///
/// Important: AuthorizationCheckRequest and UpsertPolicyRequest are positional records.
/// Their DataAnnotations are declared on primary-constructor parameters, so these tests
/// validate those constructor-parameter annotations directly instead of using
/// Validator.TryValidateObject(), which validates generated properties and can miss
/// parameter-targeted annotations on positional records.
/// </summary>
public sealed class Sprint2QaAuthorizationResultAndDtoTests
{
    [Theory]
    [InlineData(AuthorizationDenialReason.USER_NOT_FOUND)]
    [InlineData(AuthorizationDenialReason.USER_DEACTIVATED)]
    [InlineData(AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED)]
    [InlineData(AuthorizationDenialReason.RESOURCE_NOT_FOUND)]
    [InlineData(AuthorizationDenialReason.UNKNOWN_ACTION)]
    [InlineData(AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS)]
    [InlineData(AuthorizationDenialReason.OUTSIDE_MAINTENANCE_WINDOW)]
    [InlineData(AuthorizationDenialReason.UPSTREAM_SERVICE_UNAVAILABLE)]
    [InlineData(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED)]
    public void Deny_ReturnsStructuredReasonAndHumanReadableDetails(AuthorizationDenialReason reason)
    {
        var result = AuthorizationDecisionResult.Deny(reason);

        Assert.Equal(AuthorizationDecision.DENY, result.Decision);
        Assert.Equal(reason.ToString(), result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
        Assert.Null(result.ExpiresAt);
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
    }

    [Fact]
    public void Deny_CustomDetails_ArePreserved()
    {
        var result = AuthorizationDecisionResult.Deny(
            AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED,
            "custom diagnostic");

        Assert.Equal("custom diagnostic", result.Details);
    }

    [Fact]
    public void Allow_ReturnsAuthorizedWithExpiry()
    {
        var before = DateTimeOffset.UtcNow;

        var result = AuthorizationDecisionResult.Allow(TimeSpan.FromMinutes(30));

        var after = DateTimeOffset.UtcNow;
        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
        Assert.Equal("AUTHORIZED", result.Reason);
        Assert.NotNull(result.ExpiresAt);
        Assert.InRange(
            result.ExpiresAt!.Value,
            before.AddMinutes(30),
            after.AddMinutes(30).AddSeconds(1));
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
        Assert.Null(result.Details);
    }

    [Fact]
    public void ApprovalRequired_ReturnsDualApprovalRequirement()
    {
        var result = AuthorizationDecisionResult.ApprovalRequired("REQUIRES_APPROVAL");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("REQUIRES_APPROVAL", result.Reason);
        Assert.Equal(
            ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL,
            result.ApprovalRequirement);
        Assert.Null(result.ExpiresAt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    [InlineData(1440)]
    public void AuthorizationCheckRequest_ValidSessionDuration_PassesValidation(int minutes)
    {
        var request = new AuthorizationCheckRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SSH_ACCESS",
            minutes);

        Assert.Empty(ValidatePrimaryConstructorParameters(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    [InlineData(99999)]
    public void AuthorizationCheckRequest_InvalidSessionDuration_FailsValidation(int minutes)
    {
        var request = new AuthorizationCheckRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SSH_ACCESS",
            minutes);

        Assert.NotEmpty(ValidatePrimaryConstructorParameters(request));
    }

    [Fact]
    public void AuthorizationCheckRequest_DefaultSessionDuration_Is120Minutes()
    {
        var request = new AuthorizationCheckRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "READ_STATUS");

        Assert.Equal(120, request.SessionDurationMinutes);
    }

    [Theory]
    [InlineData("Developer", "VM", "DEV", "LOW", 0)]
    [InlineData("Developer", null, "DEV", "LOW", 3)]
    [InlineData("Admin", "VM", "PROD", "CRITICAL", 5)]
    public void UpsertPolicyRequest_ValidValues_PassValidation(
        string role,
        string? resourceType,
        string environment,
        string criticality,
        int level)
    {
        var request = new UpsertPolicyRequest(
            role,
            resourceType,
            environment,
            criticality,
            level,
            true);

        Assert.Empty(ValidatePrimaryConstructorParameters(request));
    }

    [Theory]
    [InlineData("", "VM", "DEV", "LOW", 3)]
    [InlineData("Developer", "VM", "", "LOW", 3)]
    [InlineData("Developer", "VM", "DEV", "", 3)]
    [InlineData("Developer", "VM", "DEV", "LOW", -1)]
    [InlineData("Developer", "VM", "DEV", "LOW", 6)]
    public void UpsertPolicyRequest_InvalidValues_FailValidation(
        string role,
        string? resourceType,
        string environment,
        string criticality,
        int level)
    {
        var request = new UpsertPolicyRequest(
            role,
            resourceType,
            environment,
            criticality,
            level,
            true);

        Assert.NotEmpty(ValidatePrimaryConstructorParameters(request));
    }

    [Fact]
    public void PolicyResponse_ExposesAllExpectedFields()
    {
        var id = Guid.NewGuid();
        var response = new PolicyResponse(
            id,
            "ADMIN",
            "VM",
            "PROD",
            "CRITICAL",
            5,
            true,
            false);

        Assert.Equal(id, response.Id);
        Assert.Equal("ADMIN", response.Role);
        Assert.Equal("VM", response.ResourceType);
        Assert.Equal("PROD", response.Environment);
        Assert.Equal("CRITICAL", response.Criticality);
        Assert.Equal(5, response.MaxAccessLevel);
        Assert.True(response.RequiresApprovalForElevated);
        Assert.False(response.IsActive);
    }

    private static IReadOnlyList<ValidationResult> ValidatePrimaryConstructorParameters<T>(T value)
        where T : notnull
    {
        var type = typeof(T);
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(item => item.GetParameters().Length)
            .First();

        var results = new List<ValidationResult>();

        foreach (var parameter in constructor.GetParameters())
        {
            var property = type.GetProperty(
                parameter.Name!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            Assert.NotNull(property);

            var propertyValue = property!.GetValue(value);
            foreach (var attribute in parameter.GetCustomAttributes<ValidationAttribute>())
            {
                var context = new ValidationContext(value)
                {
                    MemberName = property.Name
                };

                var result = attribute.GetValidationResult(propertyValue, context);
                if (result is not null && result != ValidationResult.Success)
                {
                    results.Add(result);
                }
            }
        }

        return results;
    }


}
