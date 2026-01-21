using Application.SocialMedias.Commands;
using FluentValidation;

namespace Application.SocialMedias.Validators;

public sealed class PublishTikTokVideoCommandValidator : AbstractValidator<PublishTikTokVideoCommand>
{
    private static readonly string[] ValidPrivacyLevels = 
    {
        "MUTUAL_FOLLOW_FRIENDS",
        "FOLLOWER_OF_CREATOR", 
        "SELF_ONLY",
        "PUBLIC_TO_EVERYONE"
    };

    public PublishTikTokVideoCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.SocialMediaId)
            .NotEmpty()
            .WithMessage("Social Media ID is required");

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required")
            .MaximumLength(2200)
            .WithMessage("Title must not exceed 2200 characters");

        RuleFor(x => x.PrivacyLevel)
            .NotEmpty()
            .WithMessage("Privacy level is required")
            .Must(level => ValidPrivacyLevels.Contains(level))
            .WithMessage($"Privacy level must be one of: {string.Join(", ", ValidPrivacyLevels)}");

        RuleFor(x => x.VideoUrl)
            .NotEmpty()
            .WithMessage("Video URL is required")
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out var result) 
                && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Video URL must be a valid HTTP or HTTPS URL");
    }
}
