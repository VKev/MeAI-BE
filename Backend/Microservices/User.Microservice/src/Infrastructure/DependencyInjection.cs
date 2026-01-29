using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Abstractions.Security;
using Application.Abstractions.Storage;
using Application.Abstractions.TikTok;
using Application.Abstractions.Threads;
using Domain.Repositories;
using Infrastructure.Logic.Payments;
using Infrastructure.Logic.Security;
using Infrastructure.Logic.Storage;
using Infrastructure.Repositories;
using Infrastructure.Logic.Seeding;
using Infrastructure.Logic.TikTok;
using Infrastructure.Logic.Threads;
using Microsoft.Extensions.DependencyInjection;
using SharedLibrary.Authentication;
using SharedLibrary.Configs;
using StackExchange.Redis;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddScoped<JwtTokenService>();
            services.AddScoped<IJwtTokenService, RevocationAwareJwtTokenService>();
            services.AddScoped<IPasswordHasher, PasswordHasher>();
            services.AddScoped<AdminUserSeeder>();
            services.AddScoped<SubscriptionSeeder>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IEmailRepository, EmailRepository>();
            services.AddSingleton<IStripePaymentService, StripePaymentService>();
            services.AddHttpClient("TikTok");
            services.AddScoped<ITikTokOAuthService, TikTokOAuthService>();
            services.AddHttpClient("Threads");
            services.AddScoped<IThreadsOAuthService, ThreadsOAuthService>();
            services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var env = sp.GetRequiredService<EnvironmentConfig>();
                var options = new ConfigurationOptions
                {
                    AbortOnConnectFail = false
                };
                options.EndPoints.Add(env.RedisHost, env.RedisPort);
                if (!string.IsNullOrWhiteSpace(env.RedisPassword))
                {
                    options.Password = env.RedisPassword;
                }

                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IVerificationCodeStore, RedisVerificationCodeStore>();
            return services;
        }
    }
}
