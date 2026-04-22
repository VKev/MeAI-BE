using System.Globalization;
using Application.Abstractions.Billing;
using Grpc.Core;
using SharedLibrary.Grpc.UserBilling;

namespace WebApi.Grpc;

public sealed class UserBillingGrpcService : UserBillingService.UserBillingServiceBase
{
    private readonly IBillingService _billingService;

    public UserBillingGrpcService(IBillingService billingService)
    {
        _billingService = billingService;
    }

    public override async Task<GetBalanceResponse> GetBalance(GetBalanceRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        var result = await _billingService.GetBalanceAsync(userId, context.CancellationToken);
        if (result.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.NotFound, result.Error.Description));
        }

        return new GetBalanceResponse
        {
            Balance = result.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    public override async Task<DebitCoinsResponse> DebitCoins(DebitCoinsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid amount."));
        }

        var referenceType = string.IsNullOrWhiteSpace(request.ReferenceType) ? null : request.ReferenceType;
        var referenceId = string.IsNullOrWhiteSpace(request.ReferenceId) ? null : request.ReferenceId;

        var result = await _billingService.DebitAsync(
            userId, amount, request.Reason, referenceType, referenceId, context.CancellationToken);

        if (result.IsFailure)
        {
            return new DebitCoinsResponse
            {
                Success = false,
                NewBalance = "0",
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Description
            };
        }

        return new DebitCoinsResponse
        {
            Success = true,
            NewBalance = result.Value.ToString(CultureInfo.InvariantCulture),
            ErrorCode = string.Empty,
            ErrorMessage = string.Empty
        };
    }

    public override async Task<RefundCoinsResponse> RefundCoins(RefundCoinsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid userId."));
        }

        if (!decimal.TryParse(request.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid amount."));
        }

        var referenceType = string.IsNullOrWhiteSpace(request.ReferenceType) ? null : request.ReferenceType;
        var referenceId = string.IsNullOrWhiteSpace(request.ReferenceId) ? null : request.ReferenceId;

        var result = await _billingService.RefundAsync(
            userId, amount, request.Reason, referenceType, referenceId, context.CancellationToken);

        if (result.IsFailure)
        {
            return new RefundCoinsResponse
            {
                Success = false,
                NewBalance = "0",
                AlreadyApplied = false,
                ErrorCode = result.Error.Code,
                ErrorMessage = result.Error.Description
            };
        }

        return new RefundCoinsResponse
        {
            Success = true,
            NewBalance = result.Value.NewBalance.ToString(CultureInfo.InvariantCulture),
            AlreadyApplied = result.Value.AlreadyApplied,
            ErrorCode = string.Empty,
            ErrorMessage = string.Empty
        };
    }
}
