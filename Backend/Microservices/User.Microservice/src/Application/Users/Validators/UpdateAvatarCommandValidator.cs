using Application.Users.Commands;
using FluentValidation;

namespace Application.Users.Validators;

public sealed class UpdateAvatarCommandValidator : AbstractValidator<UpdateAvatarCommand>
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    };

    public UpdateAvatarCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        
        RuleFor(x => x.FileStream)
            .NotNull()
            .WithMessage("File is required");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("File name is required");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithMessage("Content type is required")
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Content type must be one of: image/jpeg, image/png, image/gif, image/webp");

        RuleFor(x => x.ContentLength)
            .GreaterThan(0)
            .WithMessage("File cannot be empty")
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage($"File size cannot exceed {MaxFileSizeBytes / (1024 * 1024)}MB");
    }
}
