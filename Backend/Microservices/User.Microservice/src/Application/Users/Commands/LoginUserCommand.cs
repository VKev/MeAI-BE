using MediatR;
using SharedLibrary.Authentication;
using SharedLibrary.Common.ResponseModel;

namespace Application.Users.Commands;

public record LoginUserCommand(string Email, string Password) : IRequest<Result<LoginResponse>>;
