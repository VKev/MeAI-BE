using Application.Users.Commands;
using Application.Users;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using SharedLibrary.Authentication;

namespace test;

public sealed class AuthSoftDeleteTests
{
    [Fact]
    public async Task LoginWithPassword_ShouldRejectDeletedUser()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new PasswordHasher();
        var deletedUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "deleted_user",
            Email = "deleted@example.com",
            PasswordHash = passwordHasher.HashPassword("P@ssw0rd!"),
            Provider = "local",
            IsDeleted = true
        };

        dbContext.Users.Add(deletedUser);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var jwtTokenService = new Mock<IJwtTokenService>(MockBehavior.Strict);
        var handler = new LoginWithPasswordCommandHandler(
            unitOfWork,
            passwordHasher,
            jwtTokenService.Object,
            CreateConfiguration());

        var result = await handler.Handle(
            new LoginWithPasswordCommand(deletedUser.Email, "P@ssw0rd!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.AccountDeactivated");
    }

    [Fact]
    public async Task LoginWithPassword_ShouldRejectBannedUser()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new PasswordHasher();
        var bannedUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "banned_user",
            Email = "banned@example.com",
            PasswordHash = passwordHasher.HashPassword("P@ssw0rd!"),
            Provider = "local"
        };
        var bannedRole = new Role
        {
            Id = Guid.NewGuid(),
            Name = UserAuthenticationRules.BannedRoleName
        };
        var userRole = new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = bannedUser.Id,
            RoleId = bannedRole.Id
        };

        dbContext.Users.Add(bannedUser);
        dbContext.Roles.Add(bannedRole);
        dbContext.UserRoles.Add(userRole);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var jwtTokenService = new Mock<IJwtTokenService>(MockBehavior.Strict);
        var handler = new LoginWithPasswordCommandHandler(
            unitOfWork,
            passwordHasher,
            jwtTokenService.Object,
            CreateConfiguration());

        var result = await handler.Handle(
            new LoginWithPasswordCommand(bannedUser.Email, "P@ssw0rd!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.AccountBanned");
    }

    [Fact]
    public async Task RefreshToken_ShouldRejectDeactivatedUser_AndRevokeToken()
    {
        await using var dbContext = CreateDbContext();
        var deletedUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "deleted_user",
            Email = "deleted@example.com",
            PasswordHash = "hash",
            Provider = "local",
            IsDeleted = true
        };
        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = deletedUser.Id,
            TokenHash = HashToken("refresh-token"),
            AccessTokenJti = "jti-1",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        dbContext.Users.Add(deletedUser);
        dbContext.RefreshTokens.Add(refreshTokenEntity);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var jwtTokenService = new Mock<IJwtTokenService>(MockBehavior.Strict);
        var handler = new RefreshTokenCommandHandler(
            unitOfWork,
            jwtTokenService.Object,
            CreateConfiguration());

        var result = await handler.Handle(new RefreshTokenCommand("refresh-token"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.AccountDeactivated");
        refreshTokenEntity.RevokedAt.Should().NotBeNull();
        refreshTokenEntity.AccessTokenRevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteUser_ShouldRevokeOutstandingTokens()
    {
        await using var dbContext = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "active_user",
            Email = "active@example.com",
            PasswordHash = "hash",
            Provider = "local",
            IsDeleted = false
        };
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = "hash",
            AccessTokenJti = "jti-2",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        dbContext.Users.Add(user);
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var handler = new DeleteUserCommandHandler(unitOfWork);

        var result = await handler.Handle(new DeleteUserCommand(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.IsDeleted.Should().BeTrue();
        user.DeletedAt.Should().NotBeNull();
        refreshToken.RevokedAt.Should().NotBeNull();
        refreshToken.AccessTokenRevokedAt.Should().NotBeNull();
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AuthTestDbContext(options);
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:ExpirationMinutes"] = "60"
                })
            .Build();

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private sealed class AuthTestDbContext : MyDbContext
    {
        public AuthTestDbContext(DbContextOptions<MyDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<SocialMedia>();
        }
    }
}
