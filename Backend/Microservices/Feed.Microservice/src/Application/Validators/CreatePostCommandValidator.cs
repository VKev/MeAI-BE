using Application.Posts.Commands;
using FluentValidation;

namespace Application.Validators;

public sealed class CreatePostCommandValidator : AbstractValidator<CreatePostCommand>
{
    public CreatePostCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty();

        RuleFor(command => command.Content)
            .MaximumLength(5000)
            .When(command => !string.IsNullOrWhiteSpace(command.Content));

        RuleFor(command => command.ResourceIds)
            .Must(resourceIds => resourceIds is null || resourceIds.All(id => id != Guid.Empty))
            .WithMessage("Resource ids must be valid GUIDs.");
    }
}
