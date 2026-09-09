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
        /// Partitions the write endpoints' rate limit by the caller's Azure AD
        /// app-only identity ("oid" — the calling service principal's object id
        /// on a client-credentials token) instead of API key, since writes never
        /// go through the Cloudflare Worker's shared key. Falls back to client
        /// IP if that claim is somehow missing, same defensive fallback as the
        /// read path's key-based partitioning above.
        /// </summary>
        public static string GetWriteRateLimitPartitionKey(HttpContext httpContext)
        {
            var callerId = httpContext.User.FindFirst("oid")?.Value
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return !string.IsNullOrEmpty(callerId)
                ? $"aad:{callerId}"
                : $"ip:{httpContext.Connection.RemoteIpAddress}";
        }

        /// <summary>
        /// Header a trusted upstream Worker sets to the real visitor IP
        /// (sourced from Cloudflare's own edge-set, client-unforgeable
        /// CF-Connecting-IP), forwarded through both Workers to this API.
        /// Needed specifically for the comments write path: unlike the read
        /// path (where partitioning by the shared API key is correct, since
        /// the Worker really is the one caller), every commenter arrives via
        /// the same two-Worker chain with the same shared key, so
        /// GetRateLimitPartitionKey's key-based partitioning would put every
        /// commenter in the same bucket. Falls back to RemoteIpAddress (the
        /// Worker's own egress) if this header is absent — e.g. a direct
        /// call that skips the Workers entirely, which still has to pass
        /// RequireApiKey first regardless of what this header says.
        /// </summary>
        public const string RealClientIpHeaderName = "X-Real-Client-Ip";

        /// <summary>
        /// Partitions the comments-write rate limit by real visitor IP
        /// *and* article slug — tighter than a flat per-IP limit, so a
        /// genuine reader commenting on several different articles isn't
        /// penalized for it, while still capping how much a single source
        /// can drive up Cosmos RU spend or the moderation Function's daily
        /// LLM-usage quota on any one article.
        /// </summary>
        public static string GetCommentsRateLimitPartitionKey(HttpContext httpContext)
        {
            var clientIp = httpContext.Request.Headers.TryGetValue(RealClientIpHeaderName, out var forwardedIp) && !string.IsNullOrEmpty(forwardedIp)
                ? forwardedIp.ToString()
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var articleSlug = httpContext.Request.RouteValues.TryGetValue("slug", out var slugValue) && slugValue is not null
                ? slugValue.ToString()
                : "unknown";

            return $"ip:{clientIp}:slug:{articleSlug}";
        }

        /// <summary>
        /// Builds the fixed-window options for the articles rate-limit policy.
        /// Parameterized so tests can exercise the exact same construction path
        /// with a small limit instead of waiting on the real one. Reused as-is
        /// for the comments-write policy too — the shape (fixed window, oldest-
        /// first queue processing, no queueing) is generic, not article-specific
        /// despite the name.
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
