using Application.Users.Models;
using SharedLibrary.Common.ResponseModel;

namespace test.Integration.Auth;

internal sealed record ApiResultContract<T>(
    bool IsSuccess,
    bool IsFailure,
    Error Error,
    T Value);
