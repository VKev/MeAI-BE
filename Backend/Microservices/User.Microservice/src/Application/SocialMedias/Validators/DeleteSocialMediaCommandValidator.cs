using Application.SocialMedias.Commands;
using FluentValidation;

namespace Application.SocialMedias.Validators;

public sealed class DeleteSocialMediaCommandValidator : AbstractValidator<DeleteSocialMediaCommand>
{
    public DeleteSocialMediaCommandValidator()
    {
        RuleFor(x => x.SocialMediaId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
