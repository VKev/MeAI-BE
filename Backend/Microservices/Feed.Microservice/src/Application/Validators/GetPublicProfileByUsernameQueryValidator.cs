using Application.Profiles.Queries;
using FluentValidation;

namespace Application.Validators;

public sealed class GetPublicProfileByUsernameQueryValidator : AbstractValidator<GetPublicProfileByUsernameQuery>
{
    public GetPublicProfileByUsernameQueryValidator()
    {
        RuleFor(query => query.Username)
            .NotEmpty()
            .MaximumLength(100);
    }
}
