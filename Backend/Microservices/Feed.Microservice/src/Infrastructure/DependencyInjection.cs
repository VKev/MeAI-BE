using Application.Abstractions.Data;
using Application.Abstractions.Ai;
using Application.Abstractions.Notifications;
using Application.Abstractions.Resources;
using Infrastructure.Logic.Ai;
using Infrastructure.Logic.Notifications;
using Infrastructure.Logic.Resources;
using Infrastructure.Logic.Seeding;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SharedLibrary.Authentication;
using SharedLibrary.Configs;
using SharedLibrary.Grpc.AiFeed;
using SharedLibrary.Grpc.UserResources;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddScoped<FeedDemoDataSeeder>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddGrpcClient<UserResourceService.UserResourceServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddGrpcClient<AiFeedPostService.AiFeedPostServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["AiService:GrpcUrl"]
                              ?? configuration["AiService__GrpcUrl"]
                              ?? "http://ai-microservice:5005";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserResourceService, UserResourceGrpcService>();
            services.AddScoped<IAiFeedPostService, AiFeedPostGrpcService>();
            services.AddScoped<IFeedNotificationService, FeedNotificationService>();
            services.AddScoped<FeedNotificationFactory>();
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

            services.AddMassTransit(x =>
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    var env = context.GetRequiredService<EnvironmentConfig>();

                    if (env.IsRabbitMqCloud && !string.IsNullOrEmpty(env.RabbitMqUrl))
                    {
                        cfg.Host(new Uri(env.RabbitMqUrl));
                    }
                    else
                    {
                        cfg.Host(env.RabbitMqHost, (ushort)env.RabbitMqPort, "/", h =>
                        {
                            h.Username(env.RabbitMqUser);
                            h.Password(env.RabbitMqPassword);
                        });
                    }

                    cfg.ConfigureEndpoints(context);
                });
            });
            return services;
        }
    }
}
