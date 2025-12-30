namespace src.Setups;

internal static class CloudFrontSetup
{
    internal static IApplicationBuilder UseCloudFrontSchemeAdjustment(this IApplicationBuilder app)
    {
        return app.Use((ctx, next) =>
        {
            var protoHeader = ctx.Request.Headers["CloudFront-Forwarded-Proto"].FirstOrDefault()
                              ?? ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();

            var cfVisitor = ctx.Request.Headers["CF-Visitor"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(protoHeader) && !string.IsNullOrWhiteSpace(cfVisitor))
            {
                // CF-Visitor: {"scheme":"https"}
                var schemeVal = cfVisitor.Split('"').FirstOrDefault(s => s.Equals("https", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(schemeVal))
                {
                    protoHeader = schemeVal;
                }
            }

            if (!string.IsNullOrWhiteSpace(protoHeader))
            {
                var normalized = protoHeader.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                    ?.Trim();

                if (!string.IsNullOrEmpty(normalized))
                {
                    ctx.Request.Scheme = normalized;
                    ctx.Request.IsHttps = string.Equals(normalized, "https", StringComparison.OrdinalIgnoreCase);
                }
            }

            return next();
        });
    }
}
