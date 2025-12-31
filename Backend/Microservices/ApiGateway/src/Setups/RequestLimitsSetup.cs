using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace src.Setups;

internal static class RequestLimitsSetup
{
    private const long MaxBodyBytes = 104857600L; // 100 MB
    private const int MaxBodyBytesInt = 104857600;

    internal static void ConfigureRequestLimits(this IServiceCollection services)
    {
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = MaxBodyBytes;
        });

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = MaxBodyBytesInt;
            options.ValueLengthLimit = MaxBodyBytesInt;
            options.MultipartHeadersLengthLimit = MaxBodyBytesInt;
        });
    }
}
