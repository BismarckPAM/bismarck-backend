using FluentValidation;
using Identity.Service.DTOs;

namespace Identity.Service.Validators;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(8);
        RuleFor(request => request.RoleId).NotEmpty();
        RuleFor(request => request.DepartmentId).NotEmpty();
    }
}
