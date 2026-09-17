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

        // --- ApiSecurity.GetWriteRateLimitPartitionKey ---

        [Fact]
        public void GetWriteRateLimitPartitionKey_uses_oid_claim_when_present()
        {
            var httpContext = new DefaultHttpContext();
            var identity = new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("oid", "caller-object-id") });
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

            var partitionKey = ApiSecurity.GetWriteRateLimitPartitionKey(httpContext);

            Assert.Equal("aad:caller-object-id", partitionKey);
        }

        [Fact]
        public void GetWriteRateLimitPartitionKey_falls_back_to_ip_when_no_claims()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");

            var partitionKey = ApiSecurity.GetWriteRateLimitPartitionKey(httpContext);

            Assert.Equal("ip:203.0.113.5", partitionKey);
        }

        // --- ApiSecurity.GetCommentsRateLimitPartitionKey ---

        [Fact]
        public void GetCommentsRateLimitPartitionKey_uses_forwarded_ip_and_route_slug_when_present()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[ApiSecurity.RealClientIpHeaderName] = "198.51.100.7";
            httpContext.Request.RouteValues["slug"] = "my-article";

            var partitionKey = ApiSecurity.GetCommentsRateLimitPartitionKey(httpContext);

            Assert.Equal("ip:198.51.100.7:slug:my-article", partitionKey);
        }

        [Fact]
        public void GetCommentsRateLimitPartitionKey_falls_back_to_remote_ip_when_header_absent()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");
            httpContext.Request.RouteValues["slug"] = "my-article";

            var partitionKey = ApiSecurity.GetCommentsRateLimitPartitionKey(httpContext);

            Assert.Equal("ip:203.0.113.5:slug:my-article", partitionKey);
        }

        [Fact]
        public void GetCommentsRateLimitPartitionKey_falls_back_to_unknown_slug_when_route_value_missing()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[ApiSecurity.RealClientIpHeaderName] = "198.51.100.7";

            var partitionKey = ApiSecurity.GetCommentsRateLimitPartitionKey(httpContext);

            Assert.Equal("ip:198.51.100.7:slug:unknown", partitionKey);
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
        public async Task InMemoryArticleRepository_GetArticleCountAsync_is_never_smaller_than_GetArticlesAsync_count()
        {
            // Relative, not an absolute number -- InMemoryArticleRepository's
            // SeedArticles is a static list mutated by other tests'
            // CreateArticleAsync calls (a known, accepted cross-test-fixture
            // quirk, see InMemoryCommentRepository's own doc comment), so
            // this only asserts the one invariant that must hold regardless
            // of test run order: GetArticleCountAsync counts every
            // non-unlisted article including future-dated ones, so it can
            // never report fewer than GetArticlesAsync's (future-excluding)
            // list. The dedicated future-dated test below proves they can
            // actually diverge.
            var repository = new InMemoryArticleRepository();

            var count = await repository.GetArticleCountAsync();
            var articles = await repository.GetArticlesAsync();

            Assert.True(count >= articles.Count);
        }

        [Fact]
        public async Task InMemoryArticleRepository_future_dated_article_counted_but_excluded_from_list()
        {
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("future-slug", "Future Title", "Future content.", "Future summary.", DateTime.UtcNow.AddDays(7));
            var countBefore = await repository.GetArticleCountAsync();

            var created = await repository.CreateArticleAsync(request);
            var articles = await repository.GetArticlesAsync();
            var countAfter = await repository.GetArticleCountAsync();

            Assert.NotNull(created);
            Assert.DoesNotContain(articles, a => a.Slug == "future-slug"); // still excluded from the public list
            Assert.Equal(countBefore + 1, countAfter); // but counted -- unlike GetArticlesAsync's future-publishedAt exclusion
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

        [Fact]
        public async Task InMemoryArticleRepository_IncrementViewCountAsync_increments_and_persists()
        {
            var repository = new InMemoryArticleRepository();
            var before = await repository.GetArticleBySlugAsync("first-article");

            var afterFirstView = await repository.IncrementViewCountAsync("first-article");
            var afterSecondView = await repository.IncrementViewCountAsync("first-article");

            Assert.NotNull(before);
            Assert.NotNull(afterFirstView);
            Assert.NotNull(afterSecondView);
            Assert.Equal(before!.ViewCount + 1, afterFirstView!.ViewCount);
            Assert.Equal(before.ViewCount + 2, afterSecondView!.ViewCount);
            // Persisted, not just returned — a fresh read sees the same count.
            Assert.Equal(afterSecondView.ViewCount, (await repository.GetArticleBySlugAsync("first-article"))!.ViewCount);
        }

        [Fact]
        public async Task InMemoryArticleRepository_IncrementViewCountAsync_returns_null_for_unknown_slug()
        {
            var repository = new InMemoryArticleRepository();

            var result = await repository.IncrementViewCountAsync("does-not-exist");

            Assert.Null(result);
        }

        [Fact]
        public async Task InMemoryArticleRepository_CreateArticleAsync_creates_and_is_then_readable()
        {
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("new-slug", "New Title", "New content.", "New summary.", DateTime.UtcNow.AddDays(-1), new List<string> { "tag" });

            var created = await repository.CreateArticleAsync(request);

            Assert.NotNull(created);
            Assert.Equal("new-slug", created!.Slug);
            Assert.Equal(0, created.ViewCount);
            Assert.Equal("new-slug", (await repository.GetArticleBySlugAsync("new-slug"))!.Slug);
        }

        [Fact]
        public async Task InMemoryArticleRepository_unlisted_article_excluded_from_list_and_count_but_fetchable_by_slug()
        {
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("unlisted-slug", "Unlisted Title", "Unlisted content.", "Unlisted summary.", DateTime.UtcNow.AddDays(-1), Unlisted: true);
            var countBefore = await repository.GetArticleCountAsync();

            var created = await repository.CreateArticleAsync(request);
            var articles = await repository.GetArticlesAsync();
            var countAfter = await repository.GetArticleCountAsync();
            var fetchedBySlug = await repository.GetArticleBySlugAsync("unlisted-slug");

            Assert.NotNull(created);
            Assert.True(created!.Unlisted);
            Assert.DoesNotContain(articles, a => a.Slug == "unlisted-slug");
            Assert.Equal(countBefore, countAfter); // not counted either
            Assert.NotNull(fetchedBySlug);
            Assert.Equal("unlisted-slug", fetchedBySlug!.Slug);
        }

        [Fact]
        public async Task InMemoryArticleRepository_GetMostViewedArticleAsync_excludes_unlisted_articles_even_with_the_most_views()
        {
            // IncrementViewCountAsync doesn't exclude Unlisted articles (only
            // future-publishedAt), so an unlisted article really can
            // accumulate real views -- this proves GetMostViewedArticleAsync
            // still never surfaces it, since it backs a homepage link that
            // must only ever point at something publicly listed.
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("unlisted-most-viewed", "Unlisted Popular", "Content.", "Summary.", DateTime.UtcNow.AddDays(-1), Unlisted: true);
            await repository.CreateArticleAsync(request);

            for (var i = 0; i < 1000; i++)
            {
                await repository.IncrementViewCountAsync("unlisted-most-viewed");
            }

            var mostViewed = await repository.GetMostViewedArticleAsync();

            Assert.NotNull(mostViewed);
            Assert.NotEqual("unlisted-most-viewed", mostViewed!.Slug);
            Assert.True(mostViewed.ViewCount < 1000);
        }

        [Fact]
        public async Task InMemoryArticleRepository_CreateArticleAsync_returns_null_for_duplicate_slug()
        {
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("first-article", "Title", "Content.");

            var result = await repository.CreateArticleAsync(request);

            Assert.Null(result);
        }

        [Fact]
        public async Task InMemoryArticleRepository_UpdateArticleAsync_replaces_fields_and_preserves_view_count()
        {
            // SeedArticles is static, shared across every InMemoryArticleRepository
            // instance in the test process — read the current count rather than
            // assuming an absolute value, same reasoning as
            // InMemoryArticleRepository_IncrementViewCountAsync_increments_and_persists
            // above, which asserts deltas for the same reason.
            var repository = new InMemoryArticleRepository();
            var before = (await repository.GetArticleBySlugAsync("first-article"))!.ViewCount;
            var request = new ArticleWriteRequest("first-article", "Updated Title", "Updated content.", "Updated summary.", DateTime.UtcNow.AddDays(-1), new List<string> { "updated-tag" });

            var updated = await repository.UpdateArticleAsync("first-article", request);

            Assert.NotNull(updated);
            Assert.Equal("Updated Title", updated!.Title);
            Assert.Equal(before, updated.ViewCount); // preserved, not reset by the write
        }

        [Fact]
        public async Task InMemoryArticleRepository_UpdateArticleAsync_returns_null_for_unknown_slug()
        {
            var repository = new InMemoryArticleRepository();
            var request = new ArticleWriteRequest("does-not-exist", "Title", "Content.");

            var result = await repository.UpdateArticleAsync("does-not-exist", request);

            Assert.Null(result);
        }

        // --- ArticleWriteValidation ---

        [Fact]
        public void ArticleWriteValidation_passes_for_a_fully_populated_request()
        {
            var request = new ArticleWriteRequest("slug", "Title", "Content.");

            var errors = ArticleWriteValidation.Validate(request);

            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("", "Title", "Content.")]
        [InlineData("slug", "", "Content.")]
        [InlineData("slug", "Title", "")]
        public void ArticleWriteValidation_fails_when_a_required_field_is_missing(string slug, string title, string content)
        {
            var request = new ArticleWriteRequest(slug, title, content);

            var errors = ArticleWriteValidation.Validate(request);

            Assert.NotEmpty(errors);
        }

        // --- CommentWriteValidation ---

        private static readonly CommentSettings DefaultCommentSettings = new();

        [Fact]
        public void CommentWriteValidation_passes_for_a_fully_populated_request()
        {
            var request = new CommentWriteRequest("Author", "A comment.", "author@example.com");

            var errors = CommentWriteValidation.Validate(request, DefaultCommentSettings);

            Assert.Empty(errors);
        }

        [Fact]
        public void CommentWriteValidation_passes_without_an_email()
        {
            var request = new CommentWriteRequest("Author", "A comment.");

            var errors = CommentWriteValidation.Validate(request, DefaultCommentSettings);

            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("", "A comment.")]
        [InlineData("Author", "")]
        [InlineData("   ", "A comment.")]
        [InlineData("Author", "   ")]
        public void CommentWriteValidation_fails_when_a_required_field_is_missing_or_blank(string authorName, string text)
        {
            var request = new CommentWriteRequest(authorName, text);

            var errors = CommentWriteValidation.Validate(request, DefaultCommentSettings);

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void CommentWriteValidation_fails_when_authorName_exceeds_the_configured_max_length()
        {
            var settings = new CommentSettings { MaxAuthorNameLength = 5 };
            var request = new CommentWriteRequest("TooLongName", "A comment.");

            var errors = CommentWriteValidation.Validate(request, settings);

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void CommentWriteValidation_fails_when_text_exceeds_the_configured_max_length()
        {
            var settings = new CommentSettings { MaxTextLength = 5 };
            var request = new CommentWriteRequest("Author", "This comment is too long.");

            var errors = CommentWriteValidation.Validate(request, settings);

            Assert.NotEmpty(errors);
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("missing-at-sign.com")]
        public void CommentWriteValidation_fails_for_a_malformed_email(string email)
        {
            var request = new CommentWriteRequest("Author", "A comment.", email);

            var errors = CommentWriteValidation.Validate(request, DefaultCommentSettings);

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void CommentWriteValidation_fails_when_email_exceeds_the_configured_max_length()
        {
            var settings = new CommentSettings { MaxEmailLength = 10 };
            var request = new CommentWriteRequest("Author", "A comment.", "author@example.com");

            var errors = CommentWriteValidation.Validate(request, settings);

            Assert.NotEmpty(errors);
        }

        [Fact]
        public void CommentWriteValidation_Trim_trims_whitespace_and_normalizes_a_blank_email_to_null()
        {
            var request = new CommentWriteRequest("  Author  ", "  A comment.  ", "   ");

            var trimmed = CommentWriteValidation.Trim(request);

            Assert.Equal("Author", trimmed.AuthorName);
            Assert.Equal("A comment.", trimmed.Text);
            Assert.Null(trimmed.Email);
        }

        [Fact]
        public void CommentWriteValidation_Trim_trims_a_provided_email()
        {
            var request = new CommentWriteRequest("Author", "A comment.", "  author@example.com  ");

            var trimmed = CommentWriteValidation.Trim(request);

            Assert.Equal("author@example.com", trimmed.Email);
        }
    }
}
