using MediatR;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Commands;

public record RefreshTokenCommand(string RefreshToken) : IRequest<Result<LoginResponse>>;
