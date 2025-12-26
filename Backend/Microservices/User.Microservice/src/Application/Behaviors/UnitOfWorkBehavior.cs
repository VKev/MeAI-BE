using Application.Abstractions.Data;
using MediatR;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Behaviors;

/// <summary>
///     Commits the unit of work after successful command handling, keeping transaction orchestration out of controllers.
/// </summary>
public class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (!IsCommand(request)) return response;

        if (response is Result result && result.IsFailure) return response;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    private static bool IsCommand(TRequest request)
    {
        var requestType = request?.GetType();
        if (requestType == null) return false;

        return typeof(ICommand).IsAssignableFrom(requestType) ||
               requestType.GetInterfaces()
                   .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
    }
}