using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Resource.Service.Filters;

public class ModelStateValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            var failures = context.ModelState
                .Where(entry => entry.Value is not null && entry.Value.Errors.Count > 0)
                .Where(entry => !(entry.Key == "request"
                    && entry.Value!.Errors.Any(e => e.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))))
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                {
                    var propertyName = entry.Key.StartsWith("$.")
                        ? entry.Key[2..]
                        : entry.Key;

                    var errorMessage = GetErrorMessage(propertyName, error.ErrorMessage);

                    return new ValidationFailure(propertyName, errorMessage);
                }))
                .ToList();

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private static string GetErrorMessage(string propertyName, string errorMessage)
    {
        if (propertyName.Equals("criticality", StringComparison.OrdinalIgnoreCase))
            return "Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.";

        return string.IsNullOrWhiteSpace(errorMessage)
            ? "The supplied value is invalid."
            : errorMessage;
    }
}
