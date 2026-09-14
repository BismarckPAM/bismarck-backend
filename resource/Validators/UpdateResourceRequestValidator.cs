using FluentValidation;
using Resource.Service.DTOs;
using Resource.Service.Models;

namespace Resource.Service.Validators;

public class UpdateResourceRequestValidator : AbstractValidator<UpdateResourceRequest>
{
    public UpdateResourceRequestValidator()
    {
        RuleFor(request => request.Type)
            .NotEmpty()
            .WithMessage("'Type' must not be empty.")
            .MaximumLength(100);

        RuleFor(request => request.Owner)
            .NotEmpty()
            .WithMessage("'Owner' must not be empty.")
            .MaximumLength(200);

        RuleFor(request => request.Environment)
            .NotEmpty()
            .WithMessage("'Environment' must not be empty.")
            .MaximumLength(100);

        RuleFor(request => request.Criticality)
            .NotNull()
            .WithMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.")
            .IsInEnum()
            .WithMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.")
            .Must(criticality => criticality is ResourceCriticality.LOW
                or ResourceCriticality.MEDIUM
                or ResourceCriticality.HIGH
                or ResourceCriticality.CRITICAL)
            .WithMessage("Criticality must be one of: LOW, MEDIUM, HIGH, CRITICAL.");

        RuleFor(request => request.IsActive)
            .NotNull()
            .WithMessage("'IsActive' must not be empty.");
    }
}
