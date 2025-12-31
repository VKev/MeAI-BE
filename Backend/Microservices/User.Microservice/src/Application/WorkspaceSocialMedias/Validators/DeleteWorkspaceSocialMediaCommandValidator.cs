using Application.WorkspaceSocialMedias.Commands;
using FluentValidation;

namespace Application.WorkspaceSocialMedias.Validators;

public sealed class DeleteWorkspaceSocialMediaCommandValidator
    : AbstractValidator<DeleteWorkspaceSocialMediaCommand>
{
    public DeleteWorkspaceSocialMediaCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.SocialMediaId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
