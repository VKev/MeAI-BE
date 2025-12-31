using Application.Workspaces.Commands;
using FluentValidation;

namespace Application.Workspaces.Validators;

public sealed class DeleteWorkspaceCommandValidator : AbstractValidator<DeleteWorkspaceCommand>
{
    public DeleteWorkspaceCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
