using Application.Abstractions.Data;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using System;

namespace Application.Behaviors;

/// <summary>
/// Commits the unit of work after successful command handling, keeping transaction orchestration out of controllers.
/// </summary>
public class UnitOfWorkBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkBehavior(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (!IsCommand(request))
        {
            return response;
        }

        if (response is Result result && result.IsFailure)
        {
            return response;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    private static bool IsCommand(TRequest request)
    {
        var requestType = request?.GetType();
        if (requestType == null)
        {
            return false;
        }

        if (requestType.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            return true;
        }

        var requestNamespace = requestType.Namespace;
        if (string.IsNullOrWhiteSpace(requestNamespace))
        {
            return false;
        }

        return requestNamespace.Contains(".Commands", StringComparison.Ordinal);
    }
}
