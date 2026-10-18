using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resource.Service.Filters;
using Xunit;

namespace Resource.Service.Tests.Filters;

public sealed class ModelStateValidationFilterTests
{
    [Fact]
    public void OnActionExecuting_ThrowsValidationExceptionForInvalidModelState()
    {
        var context = CreateContext();
        context.ModelState.AddModelError("$.criticality", "invalid");

        var exception = Assert.Throws<ValidationException>(
            () => new ModelStateValidationFilter().OnActionExecuting(context));

        Assert.Equal("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.",
            exception.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void OnActionExecuting_UsesFallbackMessageForEmptyErrors()
    {
        var context = CreateContext();
        context.ModelState.AddModelError("name", string.Empty);

        var exception = Assert.Throws<ValidationException>(
            () => new ModelStateValidationFilter().OnActionExecuting(context));

        Assert.Equal("The supplied value is invalid.", exception.Errors.Single().ErrorMessage);
    }

    [Fact]
    public void OnActionExecuting_IgnoresValidModelStateAndMissingRequestRequiredError()
    {
        var valid = CreateContext();
        new ModelStateValidationFilter().OnActionExecuting(valid);
        Assert.True(valid.ModelState.IsValid);

        var required = CreateContext();
        required.ModelState.AddModelError("request", "request body is required");
        new ModelStateValidationFilter().OnActionExecuting(required);
        Assert.False(required.ModelState.IsValid);
    }

    private static ActionExecutingContext CreateContext() =>
        new(
            new ActionContext(
                new DefaultHttpContext(),
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            new TestController());

    private sealed class TestController : Controller;
}
