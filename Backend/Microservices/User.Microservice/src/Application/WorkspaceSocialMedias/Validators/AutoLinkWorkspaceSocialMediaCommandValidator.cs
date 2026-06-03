using Application.WorkspaceSocialMedias.Commands;
using FluentValidation;

namespace Application.WorkspaceSocialMedias.Validators;

public sealed class AutoLinkWorkspaceSocialMediaCommandValidator
    : AbstractValidator<AutoLinkWorkspaceSocialMediaCommand>
{
    public AutoLinkWorkspaceSocialMediaCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x)
            .Must(x => x.SocialMediaId.HasValue || !string.IsNullOrWhiteSpace(x.Platform))
            .WithMessage("Platform or social media id is required.");
    }
}
