using Application.Resources.Commands;
using FluentValidation;

namespace Application.Resources.Validators;

public sealed class UpdateResourceFileCommandValidator : AbstractValidator<UpdateResourceFileCommand>
{
    public UpdateResourceFileCommandValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).NotEmpty();
        RuleFor(x => x.ContentLength).GreaterThan(0);
    }
}
