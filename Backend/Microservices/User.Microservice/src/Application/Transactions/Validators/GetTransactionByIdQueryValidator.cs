using Application.Transactions.Queries;
using FluentValidation;

namespace Application.Transactions.Validators;

public sealed class GetTransactionByIdQueryValidator : AbstractValidator<GetTransactionByIdQuery>
{
    public GetTransactionByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
    }
}
