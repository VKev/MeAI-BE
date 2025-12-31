using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Domain.Repositories;
using Infrastructure.Repositories;
using Infrastructure.Seeding;
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
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IPasswordHasher, PasswordHasher>();
            services.AddScoped<AdminUserSeeder>();
            services.AddScoped<SubscriptionSeeder>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IEmailRepository, EmailRepository>();
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
