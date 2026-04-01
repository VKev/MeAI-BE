using Application.Abstractions.Notifications;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using SharedLibrary.Configs;
using StackExchange.Redis;
using WebApi.Hubs;

namespace WebApi.Setups;

public static class SignalRSetup
{
    public static void AddNotificationSignalR(this WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = builder.Environment.IsDevelopment();
            })
            .AddStackExchangeRedis(options =>
            {
                var redisHost = builder.Configuration["Redis:Host"]
                                ?? builder.Configuration["REDIS__HOST"]
                                ?? builder.Configuration["REDIS_HOST"]
                                ?? "redis";
                var redisPort = builder.Configuration["Redis:Port"]
                                ?? builder.Configuration["REDIS__PORT"]
                                ?? builder.Configuration["REDIS_PORT"]
                                ?? "6379";
                var redisPassword = builder.Configuration["Redis:Password"]
                                    ?? builder.Configuration["REDIS__PASSWORD"]
                                    ?? builder.Configuration["REDIS_PASSWORD"];

                var configuration = new ConfigurationOptions
                {
                    AbortOnConnectFail = false
                };
                configuration.EndPoints.Add(redisHost, int.Parse(redisPort));
                if (!string.IsNullOrWhiteSpace(redisPassword))
                {
                    configuration.Password = redisPassword;
                }

                options.Configuration = configuration;
            });

        builder.Services.AddScoped<INotificationRealtimeNotifier, SignalRNotificationRealtimeNotifier>();
    }

    public static void MapNotificationSignalR(this WebApplication app)
    {
        app.MapHub<NotificationHub>(NotificationHub.HubRoute);
    }
}
