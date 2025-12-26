using MediatR;
using SharedLibrary.Common.ResponseModel;
using System.ComponentModel.DataAnnotations;
using ValidationResult = SharedLibrary.Common.ResponseModel.ValidationResult;
using DataAnnotationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Application.Behaviors;

public class ValidationPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse> where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationResults = new List<DataAnnotationResult>();
        var validationContext = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
        {
            var errors = validationResults
                .SelectMany(
                    result => result.MemberNames.DefaultIfEmpty(string.Empty),
                    (result, member) => new Error(member, result.ErrorMessage ?? "Validation failed."))
                .Distinct()
                .ToArray();

            return CreateValidationResult<TResponse>(errors);
        }

        return await next();
    }

    private static TResult CreateValidationResult<TResult>(Error[] errors) where TResult : Result
    {
        if (typeof(TResult) == typeof(Result)) return (ValidationResult.WithErrors(errors) as TResult)!;

        var validationResult = typeof(ValidationResult<>)
            .GetGenericTypeDefinition()
            .MakeGenericType(typeof(TResult).GenericTypeArguments[0])
            .GetMethod(nameof(ValidationResult.WithErrors))!
            .Invoke(null, new object?[] { errors })!;

        return (TResult)validationResult;
    }
}
