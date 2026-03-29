using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Security;
using Infrastructure.Context;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace test.Integration.Auth;

public sealed class UserAuthApiFixture : IAsyncLifetime
{
    private const string DatabaseName = "user_auth_tests";
    private const string DatabaseUsername = "postgres";
    private const string DatabasePassword = "postgres";
    private const string EmailVerificationPurpose = "email_verification";

    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername(DatabaseUsername)
        .WithPassword(DatabasePassword)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.2-alpine").Build();

    internal UserAuthApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        await _redisContainer.StartAsync();

        Factory = new UserAuthApiFactory(
            _postgreSqlContainer.Hostname,
            _postgreSqlContainer.GetMappedPublicPort(5432),
            DatabaseName,
            DatabaseUsername,
            DatabasePassword,
            _redisContainer.Hostname,
            _redisContainer.GetMappedPublicPort(6379));

        using var client = CreateClient();
        await ExecuteDbContextAsync(async dbContext =>
        {
            await dbContext.Database.MigrateAsync();
            return true;
        });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }

    public HttpClient CreateClient()
    {
        return Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public async Task SeedEmailVerificationCodeAsync(string email, string code)
    {
        using var scope = Factory.Services.CreateScope();
        var verificationCodeStore = scope.ServiceProvider.GetRequiredService<IVerificationCodeStore>();
        await verificationCodeStore.StoreAsync(
            EmailVerificationPurpose,
            email.Trim().ToLowerInvariant(),
            code,
            TimeSpan.FromMinutes(5));
    }

    public async Task<T> ExecuteDbContextAsync<T>(Func<MyDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        return await action(dbContext);
    }

    public static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
