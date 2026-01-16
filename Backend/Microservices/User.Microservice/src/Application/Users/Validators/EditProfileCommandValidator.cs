using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class EditProfileCommandValidator : AbstractValidator<EditProfileCommand>
{
    public EditProfileCommandValidator()
    {
        When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber), () =>
        {
            RuleFor(x => x.PhoneNumber)
                .Matches(@"^\+?[1-9]\d{1,14}$")
                .WithMessage("Phone number must be a valid format");
        });

        When(x => x.Birthday.HasValue, () =>
        {
            RuleFor(x => x.Birthday)
                .LessThan(DateTime.UtcNow)
                .WithMessage("Birthday must be in the past")
                .GreaterThan(DateTime.UtcNow.AddYears(-150))
                .WithMessage("Birthday must be within the last 150 years");
        });

        When(x => !string.IsNullOrWhiteSpace(x.FullName), () =>
        {
            RuleFor(x => x.FullName)
                .MaximumLength(200)
                .WithMessage("Full name must not exceed 200 characters");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Address), () =>
        {
            RuleFor(x => x.Address)
                .MaximumLength(500)
                .WithMessage("Address must not exceed 500 characters");
        });
    }
}
