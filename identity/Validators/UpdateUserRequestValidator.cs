using FluentValidation;
using Identity.Service.DTOs;

namespace Identity.Service.Validators;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.RoleId).NotEmpty();
        RuleFor(request => request.DepartmentId).NotEmpty();
        RuleFor(request => request.IsActive).NotNull();
    }
}
