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

                    var errorMessage = propertyName.Equals("criticality", StringComparison.OrdinalIgnoreCase)
                        ? "Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL."
                        : string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The supplied value is invalid."
                            : error.ErrorMessage;

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
}
