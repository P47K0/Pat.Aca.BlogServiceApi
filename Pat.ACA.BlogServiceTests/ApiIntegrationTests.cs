using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pat.Aca.BlogServiceApi;
using System.Net.Http.Json;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    // Exercises the real HTTP pipeline end-to-end via WebApplicationFactory —
    // routing, middleware, DI, the works. Slower and broader than
    // ApiUnitTests.cs, which tests individual pieces (ApiSecurity,
    // InMemoryArticleRepository) directly with no host at all.
    public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestApiKey = "test-api-key";

        private readonly HttpClient _client;
        private readonly HttpClient _clientWithoutApiKey;

        public ApiIntegrationTests(WebApplicationFactory<Program> factory)
        {
            // Swap the real repository for a fake, in-memory one so tests never
            // depend on appsettings.json's (placeholder) Cosmos config or a real
            // Cosmos DB connection. Also pin a known ApiKey so tests don't depend
            // on whatever's in appsettings.Development.json either.
            var testFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", TestApiKey);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArticleRepository>();
                    services.AddSingleton<IArticleRepository, FakeArticleRepository>();
                });
            });

            _client = testFactory.CreateClient();
            _client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);

            // Separate client, deliberately never given the header — used only
            // by the two "without a key" tests below.
            _clientWithoutApiKey = testFactory.CreateClient();
        }

        // Every error response should be RFC 7807 ProblemDetails, not a bare
        // status code or a plain string body — this pins that shape down.
        private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, int expectedStatus)
        {
            Assert.Equal(expectedStatus, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(expectedStatus, problem!.Status);
        }

        [Fact]
        public async Task GET_healthz_returns_200()
        {
            var response = await _client.GetAsync("/healthz");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_returns_seeded_articles()
        {
            var response = await _client.GetAsync("/articles");

            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadFromJsonAsync<List<Article>>();

            Assert.NotEmpty(articles);
        }

        [Fact]
        public async Task GET_articles_excludes_future_dated_articles_and_sorts_newest_first()
        {
            var response = await _client.GetAsync("/articles");

            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadFromJsonAsync<List<Article>>();

            Assert.NotNull(articles);
            Assert.DoesNotContain(articles!, a => a.Slug == "future-article");
            Assert.Equal(
                articles!.OrderByDescending(a => a.PublishedAt).Select(a => a.Slug),
                articles!.Select(a => a.Slug));
        }

        [Fact]
        public async Task GET_articles_with_no_query_params_has_no_pagination_headers()
        {
            // Backward-compat pin: the plain (unpaginated) request — what
            // sitemap.xml/feed.xml/the tag cloud/api-proxy's SWR cache all
            // still send — must look exactly as it did before pagination
            // existed, headers included.
            var response = await _client.GetAsync("/articles");

            response.EnsureSuccessStatusCode();
            Assert.False(response.Headers.Contains("X-Has-More"));
            Assert.False(response.Headers.Contains("X-Next-Cursor"));
        }

        [Fact]
        public async Task GET_articles_with_limit_returns_a_page_and_has_more_header()
        {
            var response = await _client.GetAsync("/articles?limit=1");

            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadFromJsonAsync<List<Article>>();

            Assert.NotNull(articles);
            Assert.Single(articles!);
            Assert.True(response.Headers.TryGetValues("X-Has-More", out var hasMoreValues));
            Assert.Equal("true", hasMoreValues!.Single());
            Assert.True(response.Headers.TryGetValues("X-Next-Cursor", out var cursorValues));
            Assert.Equal(articles![0].Slug, cursorValues!.Single());
        }

        [Fact]
        public async Task GET_articles_with_after_cursor_returns_the_next_page()
        {
            var firstPage = await _client.GetAsync("/articles?limit=1");
            var firstPageArticles = await firstPage.Content.ReadFromJsonAsync<List<Article>>();
            var cursor = firstPage.Headers.GetValues("X-Next-Cursor").Single();

            var secondPage = await _client.GetAsync($"/articles?limit=1&after={cursor}");
            secondPage.EnsureSuccessStatusCode();
            var secondPageArticles = await secondPage.Content.ReadFromJsonAsync<List<Article>>();

            Assert.NotNull(secondPageArticles);
            Assert.Single(secondPageArticles!);
            Assert.NotEqual(firstPageArticles![0].Slug, secondPageArticles![0].Slug);
            // Don't assert a specific has-more value here: FakeArticleRepository's
            // seed list is a static field shared with ApiWriteIntegrationTests,
            // which runs concurrently and adds articles of its own — so the
            // total published count isn't stable across the whole test run.
            // Just pin the structural invariant instead: a header carrying a
            // cursor is present if and only if has-more says true.
            var hasMore = secondPage.Headers.GetValues("X-Has-More").Single();
            Assert.Contains(hasMore, new[] { "true", "false" });
            Assert.Equal(hasMore == "true", secondPage.Headers.Contains("X-Next-Cursor"));
        }

        [Fact]
        public async Task GET_articles_with_unknown_after_cursor_falls_back_to_the_first_page()
        {
            var firstPage = await _client.GetAsync("/articles?limit=1");
            var firstPageArticles = await firstPage.Content.ReadFromJsonAsync<List<Article>>();

            var response = await _client.GetAsync("/articles?limit=1&after=this-slug-does-not-exist");
            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadFromJsonAsync<List<Article>>();

            Assert.Equal(firstPageArticles![0].Slug, articles![0].Slug);
        }

        [Fact]
        public async Task GET_articles_slug_returns_200_for_valid_slug()
        {
            var response = await _client.GetAsync("/articles/first-article");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_slug_increments_view_count_on_each_request()
        {
            var first = await (await _client.GetAsync("/articles/second-article")).Content.ReadFromJsonAsync<Article>();
            var second = await (await _client.GetAsync("/articles/second-article")).Content.ReadFromJsonAsync<Article>();

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.ViewCount + 1, second!.ViewCount);
        }

        [Fact]
        public async Task GET_articles_slug_returns_404_problem_details_for_invalid_slug()
        {
            using var response = await _client.GetAsync("/articles/nonexistent-slug");

            await AssertProblemDetailsAsync(response, 404);
        }

        [Fact]
        public async Task GET_articles_slug_returns_404_problem_details_for_future_dated_article()
        {
            using var response = await _client.GetAsync("/articles/future-article");

            await AssertProblemDetailsAsync(response, 404);
        }

        [Fact]
        public async Task GET_articles_returns_401_problem_details_without_api_key()
        {
            using var response = await _clientWithoutApiKey.GetAsync("/articles");

            await AssertProblemDetailsAsync(response, 401);
        }

        [Fact]
        public async Task GET_unmatched_route_returns_404_problem_details()
        {
            using var response = await _client.GetAsync("/totally/not/a/route");

            await AssertProblemDetailsAsync(response, 404);
        }

        [Fact]
        public async Task GET_healthz_returns_200_without_api_key()
        {
            using var response = await _clientWithoutApiKey.GetAsync("/healthz");

            Assert.Equal(200, (int)response.StatusCode);
        }
    }

    public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags, int ViewCount = 0, string? LinkedinVideoEmbedUrl = null, string? SeriesName = null, int? SeriesOrder = null, List<string>? RelatedSlugs = null);
}
