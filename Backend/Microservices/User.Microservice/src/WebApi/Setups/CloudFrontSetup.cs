namespace WebApi.Setups;

public static class CloudFrontSetup
{
    public static void UseCloudFrontSchemeAdjustment(this WebApplication app)
    {
        app.Use((ctx, next) =>
        {
            var cfProto = ctx.Request.Headers["CloudFront-Forwarded-Proto"].ToString();
            if (string.Equals(cfProto, "https", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Request.Scheme = "https";
                ctx.Request.IsHttps = true;
            }

            return next();
        });
    }
}