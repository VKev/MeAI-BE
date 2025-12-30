using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedLibrary.Authentication;
using SharedLibrary.Common;

namespace Application.Users.Commands;

public sealed record LoginUserCommand(string UsernameOrEmail, string Password) : IRequest<LoginResponse?>;

public sealed class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, LoginResponse?>
{
    private readonly IRepository<User> _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;

    public LoginUserCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _repository = unitOfWork.Repository<User>();
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
    }

    public async Task<LoginResponse?> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var normalized = request.UsernameOrEmail.Trim().ToLowerInvariant();

        var user = await _repository.GetAll()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == normalized || u.Email.ToLower() == normalized,
                cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return null;
        }

        var roles = user.UserRoles
            .Select(ur => ur.Role?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accessToken = _jwtTokenService.GenerateToken(user.Id, user.Email, roles);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var expirationMinutes = _configuration.GetValue<int?>("Jwt:ExpirationMinutes") ?? 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        return new LoginResponse(
            accessToken,
            refreshToken,
            expiresAt,
            new UserInfo(
                user.Id,
                string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
                user.Email,
                roles));
    }
}
