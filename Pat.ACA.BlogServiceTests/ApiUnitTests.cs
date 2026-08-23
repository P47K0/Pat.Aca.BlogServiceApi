using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pat.Aca.BlogServiceApi;
using System.Threading.RateLimiting;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    // Fast, no-host tests: exercise ApiSecurity and InMemoryArticleRepository
    // directly with plain objects (DefaultHttpContext), never through
    // WebApplicationFactory/a running server. Contrast with the slower,
    // broader end-to-end coverage in ApiIntegrationTests.cs.
    public class ApiUnitTests
    {
        private const string ConfiguredKey = "the-real-key";

        // --- ApiSecurity.RequireApiKey ---

        [Fact]
        public async Task RequireApiKey_calls_next_when_key_matches()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Api-Key"] = ConfiguredKey;
            var invocationContext = EndpointFilterInvocationContext.Create(httpContext);

            var result = await ApiSecurity.RequireApiKey(
                invocationContext,
                _ => ValueTask.FromResult<object?>("passed-through"),
                ConfiguredKey);

            Assert.Equal("passed-through", result);
        }

        [Fact]
        public async Task RequireApiKey_returns_401_when_key_missing()
        {
            var httpContext = new DefaultHttpContext();
            var invocationContext = EndpointFilterInvocationContext.Create(httpContext);

            var result = await ApiSecurity.RequireApiKey(
                invocationContext,
                _ => throw new InvalidOperationException("next() should not be called"),
                ConfiguredKey);

            var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            Assert.Equal(401, statusResult.StatusCode);
        }

        [Fact]
        public async Task RequireApiKey_returns_401_when_key_wrong()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Api-Key"] = "wrong-key";
            var invocationContext = EndpointFilterInvocationContext.Create(httpContext);

            var result = await ApiSecurity.RequireApiKey(
                invocationContext,
                _ => throw new InvalidOperationException("next() should not be called"),
                ConfiguredKey);

            var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            Assert.Equal(401, statusResult.StatusCode);
        }

        [Fact]
        public async Task RequireApiKey_fails_closed_with_500_when_server_key_unconfigured()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Api-Key"] = "anything";
            var invocationContext = EndpointFilterInvocationContext.Create(httpContext);

            var result = await ApiSecurity.RequireApiKey(
                invocationContext,
                _ => throw new InvalidOperationException("next() should not be called"),
                configuredApiKey: null);

            var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        // --- ApiSecurity.GetRateLimitPartitionKey ---

        [Fact]
        public void GetRateLimitPartitionKey_uses_api_key_when_present()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["X-Api-Key"] = "caller-key";

            var partitionKey = ApiSecurity.GetRateLimitPartitionKey(httpContext);

            Assert.Equal("key:caller-key", partitionKey);
        }

        [Fact]
        public void GetRateLimitPartitionKey_falls_back_to_ip_when_no_key()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");

            var partitionKey = ApiSecurity.GetRateLimitPartitionKey(httpContext);

            Assert.Equal("ip:203.0.113.5", partitionKey);
        }

        // --- ApiSecurity.CreateArticlesLimiterOptions ---
        // Same construction path as production, but a tiny limit and a long
        // window, so this proves the "reject beyond the limit" behavior in
        // milliseconds instead of 60 real HTTP round trips, with no risk of
        // the window rolling over mid-test.

        [Fact]
        public async Task ArticlesLimiter_allows_requests_within_limit_and_rejects_beyond_it()
        {
            using var limiter = new FixedWindowRateLimiter(
                ApiSecurity.CreateArticlesLimiterOptions(permitLimit: 2, window: TimeSpan.FromHours(1)));

            using var first = await limiter.AcquireAsync();
            using var second = await limiter.AcquireAsync();
            using var third = await limiter.AcquireAsync();

            Assert.True(first.IsAcquired);
            Assert.True(second.IsAcquired);
            Assert.False(third.IsAcquired);
        }

        // --- InMemoryArticleRepository ---
        // No mocks needed — it's already a plain, dependency-free class.

        [Fact]
        public async Task InMemoryArticleRepository_excludes_future_dated_and_sorts_newest_first()
        {
            var repository = new InMemoryArticleRepository();

            var articles = await repository.GetArticlesAsync();

            Assert.All(articles, a => Assert.True(a.PublishedAt <= DateTime.UtcNow));
            Assert.Equal(
                articles.OrderByDescending(a => a.PublishedAt).Select(a => a.Slug),
                articles.Select(a => a.Slug));
        }

        [Fact]
        public async Task InMemoryArticleRepository_GetArticleBySlugAsync_finds_existing_article()
        {
            var repository = new InMemoryArticleRepository();

            var article = await repository.GetArticleBySlugAsync("first-article");

            Assert.NotNull(article);
            Assert.Equal("first-article", article!.Slug);
        }

        [Fact]
        public async Task InMemoryArticleRepository_GetArticleBySlugAsync_returns_null_for_unknown_slug()
        {
            var repository = new InMemoryArticleRepository();

            var article = await repository.GetArticleBySlugAsync("does-not-exist");

            Assert.Null(article);
        }
    }
}
