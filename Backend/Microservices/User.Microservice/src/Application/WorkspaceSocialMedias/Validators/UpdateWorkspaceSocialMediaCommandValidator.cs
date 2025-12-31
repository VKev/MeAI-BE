using Application.WorkspaceSocialMedias.Commands;
using FluentValidation;

namespace Application.WorkspaceSocialMedias.Validators;

public sealed class UpdateWorkspaceSocialMediaCommandValidator
    : AbstractValidator<UpdateWorkspaceSocialMediaCommand>
{
    public UpdateWorkspaceSocialMediaCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.SocialMediaId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty();
    }
}
