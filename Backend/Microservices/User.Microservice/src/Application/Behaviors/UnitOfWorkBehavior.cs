using Application.Abstractions.Data;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using System;
using System.Threading;

namespace Application.Behaviors;

internal static class UnitOfWorkExecutionScope
{
    private static readonly AsyncLocal<int> Depth = new();

    public static int Enter()
    {
        Depth.Value++;
        return Depth.Value;
    }

    public static void Exit()
    {
        Depth.Value = Math.Max(Depth.Value - 1, 0);
    }
}

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
        if (!IsCommand(request))
        {
            return await next();
        }

        var depth = UnitOfWorkExecutionScope.Enter();
        try
        {
            var response = await next();

            if (response is Result result && result.IsFailure)
            {
                return response;
            }

            if (depth == 1)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return response;
        }
        finally
        {
            UnitOfWorkExecutionScope.Exit();
        }
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
