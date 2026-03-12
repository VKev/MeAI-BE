using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Authentication;

namespace Infrastructure.Logic.Seeding;

public sealed class DefaultUserSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly DefaultUserSeedOptions _options;
    private readonly ILogger<DefaultUserSeeder> _logger;

    public DefaultUserSeeder(
        MyDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOptions<DefaultUserSeedOptions> options,
        ILogger<DefaultUserSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.Email))
        {
            _logger.LogInformation("Default user seed skipped: missing DefaultUser:Username, DefaultUser:Password, or DefaultUser:Email.");
            return;
        }

        var username = _options.Username.Trim();
        var email = _options.Email.Trim();
        var roleName = string.IsNullOrWhiteSpace(_options.RoleName) ? "USER" : _options.RoleName.Trim();
        var now = DateTime.UtcNow;

        var normalizedUsername = username.ToLowerInvariant();
        var normalizedEmail = email.ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == normalizedUsername || u.Email.ToLower() == normalizedEmail,
                cancellationToken);

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

        if (role == null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                Description = "Standard user",
                CreatedAt = now
            };

            _dbContext.Roles.Add(role);
        }

        if (existingUser == null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = _passwordHasher.HashPassword(_options.Password),
                Email = email,
                FullName = string.IsNullOrWhiteSpace(_options.FullName) ? username : _options.FullName,
                CreatedAt = now
            };

            _dbContext.Users.Add(user);
            _dbContext.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = role.Id,
                CreatedAt = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded default user {Username}.", username);
            return;
        }

        var hasRole = await _dbContext.UserRoles
            .AnyAsync(
                ur => ur.UserId == existingUser.Id && ur.RoleId == role.Id,
                cancellationToken);

        if (!hasRole)
        {
            _dbContext.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = existingUser.Id,
                RoleId = role.Id,
                CreatedAt = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Default user {Username} existed; assigned {Role} role.", existingUser.Username, roleName);
            return;
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Default user {Username} already exists; seeding skipped.", existingUser.Username);
    }
}
