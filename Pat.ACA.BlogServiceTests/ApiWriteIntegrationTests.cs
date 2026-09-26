using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pat.Aca.BlogServiceApi;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    // Exercises POST/PUT /articles end-to-end, including the Azure AD app-role
    // authorization now wired up for the write path. Separate from
    // ApiIntegrationTests.cs (the read path, API-key auth) since the write
    // path needs its own auth stand-in — see TestAuthHandler.
    public class ApiWriteIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApiWriteIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArticleRepository>();
                    services.AddSingleton<IArticleRepository, FakeArticleRepository>();

                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                });
            });
        }

        private HttpClient CreateClient(params string[] roles)
        {
            var client = _factory.CreateClient();
            // Unique per client so each test gets its own write-rate-limit
            // partition (ApiSecurity.GetWriteRateLimitPartitionKey) instead of
            // sharing one budget across the whole fixture.
            client.DefaultRequestHeaders.Add(TestAuthHandler.CallerIdHeaderName, Guid.NewGuid().ToString());
            // TestAuthHandler treats this header's absence as "unauthenticated"
            // (used for the 401 tests, via _factory.CreateClient() directly
            // instead of this helper) and its presence as "authenticated with
            // these roles". To exercise "authenticated but missing the role"
            // (403), callers must pass a real placeholder role here — an empty
            // string value doesn't reliably reach the server as a present
            // header, so it can't be used to mean "authenticated, zero roles".
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, string.Join(",", roles));

            return client;
        }

        private static ArticleWriteRequest ValidRequest(string slug) =>
            new(slug, $"Title for {slug}", "Some content.", "A summary.", DateTime.UtcNow.AddDays(-1), new List<string> { "tag1" });

        [Fact]
        public async Task POST_articles_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/articles", ValidRequest("no-auth-post"));

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_articles_without_required_role_returns_403()
        {
            var client = CreateClient("Some.Other.Role"); // authenticated, wrong role

            using var response = await client.PostAsJsonAsync("/articles", ValidRequest("wrong-role-post"));

            Assert.Equal((int)HttpStatusCode.Forbidden, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_articles_creates_article_and_returns_201()
        {
            var client = CreateClient("Articles.Write");

            using var response = await client.PostAsJsonAsync("/articles", ValidRequest("new-write-article"));

            Assert.Equal((int)HttpStatusCode.Created, (int)response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(created);
            Assert.Equal("new-write-article", created!.Slug);
            Assert.Equal(0, created.ViewCount);
        }

        [Fact]
        public async Task POST_articles_returns_409_for_duplicate_slug()
        {
            var client = CreateClient("Articles.Write");
            await client.PostAsJsonAsync("/articles", ValidRequest("duplicate-slug-article"));

            using var response = await client.PostAsJsonAsync("/articles", ValidRequest("duplicate-slug-article"));

            Assert.Equal((int)HttpStatusCode.Conflict, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_articles_returns_400_for_missing_required_fields()
        {
            var client = CreateClient("Articles.Write");

            using var response = await client.PostAsJsonAsync("/articles", new ArticleWriteRequest("", "", ""));

            Assert.Equal((int)HttpStatusCode.BadRequest, (int)response.StatusCode);
        }

        [Fact]
        public async Task PUT_articles_slug_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            using var response = await client.PutAsJsonAsync("/articles/first-article", ValidRequest("first-article"));

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_articles_persists_linkedin_video_embed_url()
        {
            var client = CreateClient("Articles.Write");
            var request = ValidRequest("video-embed-article") with
            {
                LinkedinVideoEmbedUrl = "https://www.linkedin.com/embed/feed/update/urn:li:ugcPost:1234567890?compact=1"
            };

            using var response = await client.PostAsJsonAsync("/articles", request);

            var created = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(created);
            Assert.Equal(request.LinkedinVideoEmbedUrl, created!.LinkedinVideoEmbedUrl);
        }

        [Fact]
        public async Task PUT_articles_slug_can_add_a_linkedin_video_embed_url_to_an_existing_article()
        {
            var client = CreateClient("Articles.Write");
            await client.PostAsJsonAsync("/articles", ValidRequest("video-embed-added-later"));

            using var response = await client.PutAsJsonAsync(
                "/articles/video-embed-added-later",
                ValidRequest("video-embed-added-later") with
                {
                    LinkedinVideoEmbedUrl = "https://www.linkedin.com/embed/feed/update/urn:li:ugcPost:1234567890?compact=1"
                });

            var updated = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(updated);
            Assert.Equal("https://www.linkedin.com/embed/feed/update/urn:li:ugcPost:1234567890?compact=1", updated!.LinkedinVideoEmbedUrl);
        }

        [Fact]
        public async Task POST_articles_persists_cover_image_url()
        {
            var client = CreateClient("Articles.Write");
            var request = ValidRequest("cover-image-article") with
            {
                CoverImageUrl = "https://images.koorevaar.com/covers/cover-image-article.jpg"
            };

            using var response = await client.PostAsJsonAsync("/articles", request);

            var created = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(created);
            Assert.Equal(request.CoverImageUrl, created!.CoverImageUrl);
        }

        [Fact]
        public async Task POST_articles_returns_400_for_a_non_https_cover_image_url()
        {
            var client = CreateClient("Articles.Write");
            var request = ValidRequest("bad-cover-image-article") with { CoverImageUrl = "javascript:alert(1)" };

            using var response = await client.PostAsJsonAsync("/articles", request);

            Assert.Equal((int)HttpStatusCode.BadRequest, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_articles_persists_series_and_related_slugs()
        {
            var client = CreateClient("Articles.Write");
            var request = ValidRequest("series-article-part-1") with
            {
                SeriesName = "Debugging Skills",
                SeriesOrder = 1,
                RelatedSlugs = new List<string> { "series-article-part-2", "series-article-part-3" }
            };

            using var response = await client.PostAsJsonAsync("/articles", request);

            var created = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(created);
            Assert.Equal("Debugging Skills", created!.SeriesName);
            Assert.Equal(1, created.SeriesOrder);
            Assert.Equal(request.RelatedSlugs, created.RelatedSlugs);
        }

        [Fact]
        public async Task PUT_articles_slug_can_add_related_slugs_to_an_existing_article()
        {
            var client = CreateClient("Articles.Write");
            await client.PostAsJsonAsync("/articles", ValidRequest("related-slugs-added-later"));

            using var response = await client.PutAsJsonAsync(
                "/articles/related-slugs-added-later",
                ValidRequest("related-slugs-added-later") with
                {
                    RelatedSlugs = new List<string> { "first-article", "second-article" }
                });

            var updated = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(updated);
            Assert.Equal(new List<string> { "first-article", "second-article" }, updated!.RelatedSlugs);
        }

        [Fact]
        public async Task PUT_articles_slug_updates_and_preserves_view_count()
        {
            var client = CreateClient("Articles.Write");
            await client.PostAsJsonAsync("/articles", ValidRequest("view-count-preserved"));

            using var response = await client.PutAsJsonAsync(
                "/articles/view-count-preserved",
                ValidRequest("view-count-preserved") with { Title = "Updated Title" });

            Assert.Equal((int)HttpStatusCode.OK, (int)response.StatusCode);
            var updated = await response.Content.ReadFromJsonAsync<Article>();
            Assert.NotNull(updated);
            Assert.Equal("Updated Title", updated!.Title);
            // Never touched by the write path — a freshly-created article
            // starts at 0 and PUT must not reset it.
            Assert.Equal(0, updated.ViewCount);
        }

        [Fact]
        public async Task PUT_articles_slug_returns_404_for_unknown_slug()
        {
            var client = CreateClient("Articles.Write");

            using var response = await client.PutAsJsonAsync("/articles/does-not-exist", ValidRequest("does-not-exist"));

            Assert.Equal((int)HttpStatusCode.NotFound, (int)response.StatusCode);
        }

        [Fact]
        public async Task PUT_articles_slug_returns_400_when_body_slug_does_not_match_route()
        {
            var client = CreateClient("Articles.Write");
            await client.PostAsJsonAsync("/articles", ValidRequest("route-mismatch-article"));

            using var response = await client.PutAsJsonAsync("/articles/route-mismatch-article", ValidRequest("different-slug"));

            Assert.Equal((int)HttpStatusCode.BadRequest, (int)response.StatusCode);
        }
    }
}
