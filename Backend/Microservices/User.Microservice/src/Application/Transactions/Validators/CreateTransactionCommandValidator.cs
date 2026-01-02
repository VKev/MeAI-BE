using Application.Transactions.Commands;
using FluentValidation;

namespace Application.Transactions.Validators;

public sealed class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.TransactionType)
            .NotEmpty()
            .WithMessage("Transaction type is required.");

        RuleFor(x => x.RelationType)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .When(x => x.RelationType != null)
            .WithMessage("Relation type cannot be empty.");

        RuleFor(x => x.Cost)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Cost.HasValue);

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
}
