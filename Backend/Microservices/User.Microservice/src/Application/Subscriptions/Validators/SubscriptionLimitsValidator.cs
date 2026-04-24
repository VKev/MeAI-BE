using Domain.Entities;
using FluentValidation;

namespace Application.Subscriptions.Validators;

internal sealed class SubscriptionLimitsValidator : AbstractValidator<SubscriptionLimits>
{
    public SubscriptionLimitsValidator()
    {
        RuleFor(x => x.NumberOfSocialAccounts)
            .GreaterThanOrEqualTo(0)
            .When(x => x.NumberOfSocialAccounts.HasValue);

        RuleFor(x => x.RateLimitForContentCreation)
            .GreaterThanOrEqualTo(0)
            .When(x => x.RateLimitForContentCreation.HasValue);

        RuleFor(x => x.NumberOfWorkspaces)
            .GreaterThanOrEqualTo(0)
            .When(x => x.NumberOfWorkspaces.HasValue);

        RuleFor(x => x.MaxPagesPerSocialAccount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaxPagesPerSocialAccount.HasValue);

        RuleFor(x => x.StorageQuotaBytes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.StorageQuotaBytes.HasValue);

        RuleFor(x => x.MaxUploadFileBytes)
            .GreaterThan(0)
            .When(x => x.MaxUploadFileBytes.HasValue);

        RuleFor(x => x.RetentionDaysAfterDelete)
            .GreaterThanOrEqualTo(0)
            .When(x => x.RetentionDaysAfterDelete.HasValue);

        RuleFor(x => x)
            .Must(x =>
                !x.StorageQuotaBytes.HasValue ||
                !x.MaxUploadFileBytes.HasValue ||
                x.StorageQuotaBytes.Value == 0 ||
                x.MaxUploadFileBytes.Value <= x.StorageQuotaBytes.Value)
            .WithMessage("max_upload_file_bytes cannot exceed storage_quota_bytes.");
    }
}
