using Application.Transactions.Commands;
using FluentValidation;

namespace Application.Transactions.Validators;

public sealed class PatchTransactionCommandValidator : AbstractValidator<PatchTransactionCommand>
{
    public PatchTransactionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        RuleFor(x => x)
            .Must(HasAnyChanges)
            .WithMessage("At least one field must be provided for patch.");

        RuleFor(x => x.UserId)
            .Must(id => id != Guid.Empty)
            .When(x => x.UserId.HasValue);

        RuleFor(x => x.RelationId)
            .Must(id => id != Guid.Empty)
            .When(x => x.RelationId.HasValue);

        RuleFor(x => x.RelationType)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .When(x => x.RelationType != null)
            .WithMessage("Relation type cannot be empty.");

        RuleFor(x => x.Cost)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Cost.HasValue);

        RuleFor(x => x.TransactionType)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .When(x => x.TransactionType != null)
            .WithMessage("Transaction type cannot be empty.");

        RuleFor(x => x.TokenUsed)
            .GreaterThanOrEqualTo(0)
            .When(x => x.TokenUsed.HasValue);

        RuleFor(x => x.PaymentMethod)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .When(x => x.PaymentMethod != null)
            .WithMessage("Payment method cannot be empty.");

        RuleFor(x => x.Status)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .When(x => x.Status != null)
            .WithMessage("Status cannot be empty.");
    }

    private static bool HasAnyChanges(PatchTransactionCommand command)
    {
        return command.UserId.HasValue
            || command.RelationId.HasValue
            || command.RelationType != null
            || command.Cost.HasValue
            || command.TransactionType != null
            || command.TokenUsed.HasValue
            || command.PaymentMethod != null
            || command.Status != null;
    }
}
