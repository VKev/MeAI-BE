using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Authentication;

namespace Infrastructure.Logic.Seeding;

public sealed class AdminUserSeeder
{
    private readonly MyDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AdminSeedOptions _options;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(
        MyDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOptions<AdminSeedOptions> options,
        ILogger<AdminUserSeeder> logger)
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
            _logger.LogInformation("Admin seed skipped: missing Admin:Username, Admin:Password, or Admin:Email.");
            return;
        }

        var adminUsername = _options.Username.Trim();
        var adminEmail = _options.Email.Trim();
        var roleName = string.IsNullOrWhiteSpace(_options.RoleName) ? "Admin" : _options.RoleName.Trim();
        var now = DateTime.UtcNow;

        var normalizedUsername = adminUsername.ToLowerInvariant();
        var normalizedEmail = adminEmail.ToLowerInvariant();

        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == normalizedUsername || u.Email.ToLower() == normalizedEmail,
                cancellationToken);

        var adminRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

        if (adminRole == null)
        {
            adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                Description = "System administrator",
                CreatedAt = now
            };

            _dbContext.Roles.Add(adminRole);
        }

        if (existingUser == null)
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Username = adminUsername,
                PasswordHash = _passwordHasher.HashPassword(_options.Password),
                Email = adminEmail,
                FullName = string.IsNullOrWhiteSpace(_options.FullName) ? adminUsername : _options.FullName,
                CreatedAt = now
            };

            _dbContext.Users.Add(adminUser);
            _dbContext.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = adminUser.Id,
                RoleId = adminRole.Id,
                CreatedAt = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded admin user {Username}.", adminUsername);
            return;
        }

        var hasAdminRole = await _dbContext.UserRoles
            .AnyAsync(
                ur => ur.UserId == existingUser.Id && ur.RoleId == adminRole.Id,
                cancellationToken);

        if (!hasAdminRole)
        {
            _dbContext.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = existingUser.Id,
                RoleId = adminRole.Id,
                CreatedAt = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Admin user {Username} existed; assigned {Role} role.", existingUser.Username, roleName);
            return;
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Admin user {Username} already exists; seeding skipped.", existingUser.Username);
    }
}

