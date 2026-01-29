using Application.Abstractions;
using Domain.Repositories;
using Infrastructure.Logic.Consumers;
using Infrastructure.Repositories;
using Infrastructure.Logic.Sagas;
using Infrastructure.Logic.Services;
using MassTransit;
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
            services.AddHttpClient<IVeoVideoService, VeoVideoService>();

            services.AddScoped<IVideoTaskRepository, VideoTaskRepository>();
            services.AddScoped<IPostRepository, PostRepository>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Application.AssemblyReference).Assembly));

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
                x.AddConsumer<SubmitVideoTaskConsumer>();
                x.AddConsumer<ExtendVideoTaskConsumer>();
                x.AddConsumer<VideoCompletedConsumer>();
                x.AddConsumer<VideoFailedConsumer>();

                x.AddSagaStateMachine<VideoTaskStateMachine, VideoTaskState>()
                    .RedisRepository(r =>
                    {
                        r.KeyPrefix = "video-saga:";
                    });

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

                    cfg.UseMessageScheduler(new Uri("queue:video-scheduler"));

                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }
    }
}

