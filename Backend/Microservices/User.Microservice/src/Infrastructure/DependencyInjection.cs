using Application.Abstractions.Data;
using Infrastructure.Repositories;
using Infrastructure.Seeding;
using Microsoft.Extensions.DependencyInjection;
using SharedLibrary.Authentication;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IPasswordHasher, PasswordHasher>();
            services.AddScoped<AdminUserSeeder>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            return services;
        }
    }
}
