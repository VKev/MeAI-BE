using Application.Workspaces.Commands;
using FluentValidation;

namespace Application.Workspaces.Validators;

public sealed class CreateWorkspaceCommandValidator : AbstractValidator<CreateWorkspaceCommand>
{
    public CreateWorkspaceCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
    }
}
