using Application.WorkspaceSocialMedias.Commands;
using FluentValidation;

namespace Application.WorkspaceSocialMedias.Validators;

public sealed class CreateWorkspaceSocialMediaCommandValidator
    : AbstractValidator<CreateWorkspaceSocialMediaCommand>
{
    public CreateWorkspaceSocialMediaCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.SocialMediaId).NotEmpty();
    }
}
