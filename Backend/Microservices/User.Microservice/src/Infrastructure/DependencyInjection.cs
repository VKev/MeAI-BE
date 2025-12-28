using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Domain.Repositories;
using Infrastructure.Common;
using Infrastructure.Configs;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using SharedLibrary.Configs;
using StackExchange.Redis;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IEmailRepository, EmailRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<ISocialMediaRepository, SocialMediaRepository>();
        services.AddScoped<IWorkspaceSocialMediaRepository, WorkspaceSocialMediaRepository>();
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddSingleton<EnvironmentConfig>();

        using var provider = services.BuildServiceProvider();
        var env = provider.GetRequiredService<EnvironmentConfig>();
        var redisConnection = $"{env.RedisHost}:{env.RedisPort},password={env.RedisPassword}";
        var multiplexer = ConnectionMultiplexer.Connect(redisConnection);

        services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer);
        services.AddSingleton<IVerificationCodeStore, RedisVerificationCodeStore>();

        services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.SetKebabCaseEndpointNameFormatter();
            busConfigurator.AddConsumer<Application.Consumers.WelcomeEmailConsumer>();

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                if (env.IsRabbitMqCloud)
                    cfg.Host(env.RabbitMqUrl);
                else
                    cfg.Host(new Uri($"rabbitmq://{env.RabbitMqHost}:{env.RabbitMqPort}/"), h =>
                    {
                        h.Username(env.RabbitMqUser);
                        h.Password(env.RabbitMqPassword);
                    });

                cfg.ConfigureEndpoints(context);
            });
        });
        return services;
    }
}
