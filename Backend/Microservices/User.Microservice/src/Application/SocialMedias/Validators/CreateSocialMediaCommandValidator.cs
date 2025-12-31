using Application.SocialMedias.Commands;
using FluentValidation;

namespace Application.SocialMedias.Validators;

public sealed class CreateSocialMediaCommandValidator : AbstractValidator<CreateSocialMediaCommand>
{
    public CreateSocialMediaCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty();
    }
}
