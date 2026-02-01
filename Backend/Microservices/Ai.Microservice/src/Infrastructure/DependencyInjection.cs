using Application.Abstractions;
using Application.Abstractions.Facebook;
using Application.Abstractions.Gemini;
using Application.Abstractions.Instagram;
using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Abstractions.Threads;
using Domain.Repositories;
using Infrastructure.Logic.Consumers;
using Infrastructure.Logic.Facebook;
using Infrastructure.Logic.Gemini;
using Infrastructure.Logic.Instagram;
using Infrastructure.Logic.Threads;
using Infrastructure.Logic.Resources;
using Infrastructure.Logic.SocialMedias;
using Infrastructure.Repositories;
using Infrastructure.Logic.Sagas;
using Infrastructure.Logic.Services;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedLibrary.Authentication;
using SharedLibrary.Configs;
using SharedLibrary.Grpc.UserResources;
using StackExchange.Redis;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddHttpClient<IVeoVideoService, VeoVideoService>();
            services.AddHttpClient("Gemini");
            services.AddHttpClient("Facebook");
            services.AddHttpClient("Instagram");
            services.AddHttpClient("Threads");
            services.AddScoped<IGeminiCaptionService, GeminiCaptionService>();
            services.AddScoped<IFacebookPublishService, FacebookPublishService>();
            services.AddScoped<IInstagramPublishService, InstagramPublishService>();
            services.AddScoped<IThreadsPublishService, ThreadsPublishService>();

            services.AddGrpcClient<UserResourceService.UserResourceServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserResourceService, UserResourceGrpcService>();

            services.AddGrpcClient<UserSocialMediaService.UserSocialMediaServiceClient>((sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var grpcUrl = configuration["UserService:GrpcUrl"]
                              ?? configuration["UserService__GrpcUrl"]
                              ?? "http://user-microservice:5004";
                options.Address = new Uri(grpcUrl);
            });
            services.AddScoped<IUserSocialMediaService, UserSocialMediaGrpcService>();

            services.AddScoped<IVideoTaskRepository, VideoTaskRepository>();
            services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
            services.AddScoped<IChatRepository, ChatRepository>();
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
                        r.ConnectionFactory(provider => provider.GetRequiredService<IConnectionMultiplexer>());
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

