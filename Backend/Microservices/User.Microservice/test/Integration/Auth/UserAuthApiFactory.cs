using Application.Abstractions.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace test.Integration.Auth;

internal sealed class UserAuthApiFactory(
    string databaseHost,
    int databasePort,
    string databaseName,
    string databaseUsername,
    string databasePassword,
    string redisHost,
    int redisPort)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = databaseHost,
                ["Database:Port"] = databasePort.ToString(),
                ["Database:Name"] = databaseName,
                ["Database:Username"] = databaseUsername,
                ["Database:Password"] = databasePassword,
                ["Database:Provider"] = "postgres",
                ["DATABASE_SSLMODE"] = "Disable",
                ["Redis:Host"] = redisHost,
                ["Redis:Port"] = redisPort.ToString(),
                ["Redis:Password"] = string.Empty,
                ["Jwt:SecretKey"] = "IntegrationTestsSecretKeyThatIsAtLeast32Chars!",
                ["Jwt:Issuer"] = "UserMicroservice",
                ["Jwt:Audience"] = "MicroservicesApp",
                ["Jwt:ExpirationMinutes"] = "60",
                ["AutoApply:Migrations"] = "true",
                ["Admin:Username"] = string.Empty,
                ["Admin:Password"] = string.Empty,
                ["Admin:Email"] = string.Empty,
                ["DefaultUser:Username"] = string.Empty,
                ["DefaultUser:Password"] = string.Empty,
                ["DefaultUser:Email"] = string.Empty
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IObjectStorageService>();
            services.AddSingleton<IObjectStorageService, StubObjectStorageService>();
        });
    }

    private sealed class StubObjectStorageService : IObjectStorageService
    {
        public Task<Result<bool>> DeleteAsync(string keyOrUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(true));
        }

        public Result<string> GetPresignedUrl(string keyOrUrl, TimeSpan? expiresIn = null)
        {
            return Result.Success("https://example.test/presigned");
        }

        public Task<Result<StorageObjectMetadata>> GetMetadataAsync(
            string keyOrUrl,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(new StorageObjectMetadata(0, null)));
        }

        public Task<Result<StorageUploadResult>> UploadAsync(
            StorageUploadRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(new StorageUploadResult(request.Key, $"https://example.test/{request.Key}")));
        }
    }
}
