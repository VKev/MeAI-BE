using Application.SocialMedias.Commands;
using FluentValidation;

namespace Application.SocialMedias.Validators;

public sealed class UpdateSocialMediaCommandValidator : AbstractValidator<UpdateSocialMediaCommand>
{
    public UpdateSocialMediaCommandValidator()
    {
        RuleFor(x => x.SocialMediaId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty();
    }
}
