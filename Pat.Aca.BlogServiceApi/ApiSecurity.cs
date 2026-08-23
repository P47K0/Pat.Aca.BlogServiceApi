using System.Threading.RateLimiting;

namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// The article endpoints' API-key check and rate-limit partitioning, pulled
    /// out of Program.cs so they're unit-testable without a running host
    /// (WebApplicationFactory) — see ApiUnitTests.cs in the test project.
    /// </summary>
    public static class ApiSecurity
    {
        public const string ApiKeyHeaderName = "X-Api-Key";

        /// <summary>
        /// Guards an endpoint with the shared-secret key. An unconfigured
        /// server-side key fails closed (500) rather than silently letting
        /// everything through — an empty key must never be read as "auth disabled".
        /// </summary>
        public static async ValueTask<object?> RequireApiKey(
            EndpointFilterInvocationContext context,
            EndpointFilterDelegate next,
            string? configuredApiKey)
        {
            if (string.IsNullOrEmpty(configuredApiKey))
            {
                return Results.Problem("Server is missing API key configuration", statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
                || !string.Equals(providedKey, configuredApiKey, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            return await next(context);
        }

        /// <summary>
        /// Partitions rate limiting by the caller's API key when present (the
        /// Worker is the one real caller, so this throttles it as a whole
        /// regardless of Cloudflare's shared egress IPs); falls back to client IP
        /// for unauthenticated requests, which still need throttling so the key
        /// check itself can't be brute-forced unbounded.
        /// </summary>
        public static string GetRateLimitPartitionKey(HttpContext httpContext) =>
            httpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey) && !string.IsNullOrEmpty(providedKey)
                ? $"key:{providedKey}"
                : $"ip:{httpContext.Connection.RemoteIpAddress}";

        /// <summary>
        /// Builds the fixed-window options for the articles rate-limit policy.
        /// Parameterized so tests can exercise the exact same construction path
        /// with a small limit instead of waiting on the real one.
        /// </summary>
        public static FixedWindowRateLimiterOptions CreateArticlesLimiterOptions(int permitLimit, TimeSpan window) => new()
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        };
    }
}
