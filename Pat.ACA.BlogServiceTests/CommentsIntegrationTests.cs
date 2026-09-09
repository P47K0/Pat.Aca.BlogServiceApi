using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pat.Aca.BlogServiceApi;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    // The public, anonymous comment endpoints (POST/GET .../comments) — API
    // key auth, same shape as ApiIntegrationTests.cs. The AAD-gated
    // Comments.Moderate endpoints have their own test class,
    // CommentsModerationIntegrationTests, mirroring the
    // ApiIntegrationTests/ApiWriteIntegrationTests split by auth mechanism.
    public class CommentsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestApiKey = "test-api-key";

        private readonly WebApplicationFactory<Program> _factory;

        public CommentsIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", TestApiKey);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArticleRepository>();
                    services.AddSingleton<IArticleRepository, FakeArticleRepository>();
                    services.RemoveAll<ICommentRepository>();
                    services.AddSingleton<ICommentRepository, FakeCommentRepository>();
                });
            });
        }

        // Every client gets a unique X-Real-Client-Ip so the comments-write
        // rate limiter (partitioned by ip+slug — see
        // ApiSecurity.GetCommentsRateLimitPartitionKey) never collides
        // across tests sharing this class's WebApplicationFactory fixture,
        // even when several tests target the same article slug. Mirrors
        // ApiWriteIntegrationTests.CreateClient's unique-caller-id-per-test
        // pattern for the AAD rate limiter.
        private HttpClient CreateClient(bool includeApiKey = true)
        {
            var client = _factory.CreateClient();
            if (includeApiKey)
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
            }

            client.DefaultRequestHeaders.Add(ApiSecurity.RealClientIpHeaderName, Guid.NewGuid().ToString());
            return client;
        }

        [Fact]
        public async Task GET_comments_returns_only_published_comments()
        {
            var client = CreateClient();

            var response = await client.GetAsync("/articles/first-article/comments");

            response.EnsureSuccessStatusCode();
            var comments = await response.Content.ReadFromJsonAsync<List<PublicComment>>();

            Assert.NotNull(comments);
            Assert.Single(comments!);
            Assert.Equal("published-comment-1", comments![0].Id);
        }

        [Fact]
        public async Task GET_comments_response_omits_email_and_status()
        {
            var client = CreateClient();

            var response = await client.GetAsync("/articles/first-article/comments");

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var firstComment = document.RootElement.EnumerateArray().First();

            Assert.False(firstComment.TryGetProperty("email", out _));
            Assert.False(firstComment.TryGetProperty("status", out _));
            Assert.False(firstComment.TryGetProperty("articleSlug", out _));
            Assert.False(firstComment.TryGetProperty("llmScore", out _));
        }

        [Fact]
        public async Task GET_comments_returns_404_for_unknown_article()
        {
            var client = CreateClient();

            var response = await client.GetAsync("/articles/does-not-exist/comments");

            Assert.Equal((int)HttpStatusCode.NotFound, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_comments_without_api_key_returns_401()
        {
            var client = CreateClient(includeApiKey: false);

            var response = await client.GetAsync("/articles/first-article/comments");

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_comments_creates_a_queued_comment_and_returns_201()
        {
            var client = CreateClient();
            var request = new CommentWriteRequest("New Author", "A new comment.", "author@example.com");

            var response = await client.PostAsJsonAsync("/articles/second-article/comments", request);

            Assert.Equal((int)HttpStatusCode.Created, (int)response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<Comment>();
            Assert.NotNull(created);
            Assert.Equal("New Author", created!.AuthorName);
            Assert.Equal(CommentStatus.Queued, created.Status);
            Assert.Null(created.LlmScore);
        }

        [Fact]
        public async Task POST_comments_trims_whitespace()
        {
            var client = CreateClient();
            var request = new CommentWriteRequest("  Spacey Author  ", "  A comment with padding.  ");

            var response = await client.PostAsJsonAsync("/articles/second-article/comments", request);

            var created = await response.Content.ReadFromJsonAsync<Comment>();
            Assert.NotNull(created);
            Assert.Equal("Spacey Author", created!.AuthorName);
            Assert.Equal("A comment with padding.", created.Text);
        }

        [Fact]
        public async Task POST_comments_returns_400_for_blank_fields()
        {
            var client = CreateClient();

            var response = await client.PostAsJsonAsync("/articles/second-article/comments", new CommentWriteRequest("", ""));

            Assert.Equal((int)HttpStatusCode.BadRequest, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_comments_returns_404_for_unknown_article()
        {
            var client = CreateClient();

            var response = await client.PostAsJsonAsync(
                "/articles/does-not-exist/comments",
                new CommentWriteRequest("Author", "A comment."));

            Assert.Equal((int)HttpStatusCode.NotFound, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_comments_without_api_key_returns_401()
        {
            var client = CreateClient(includeApiKey: false);

            var response = await client.PostAsJsonAsync(
                "/articles/second-article/comments",
                new CommentWriteRequest("Author", "A comment."));

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task POST_comments_is_rate_limited_per_ip_and_slug()
        {
            // A single client (one X-Real-Client-Ip, from CreateClient) posting
            // to the same slug more than the 2/hour comments-write limit —
            // exercises ApiSecurity.GetCommentsRateLimitPartitionKey's actual
            // wiring end to end, not just the partition-key function in
            // isolation (already covered in ApiUnitTests.cs).
            var client = CreateClient();
            var request = new CommentWriteRequest("Author", "A comment.");

            using var first = await client.PostAsJsonAsync("/articles/first-article/comments", request);
            using var second = await client.PostAsJsonAsync("/articles/first-article/comments", request);
            using var third = await client.PostAsJsonAsync("/articles/first-article/comments", request);

            Assert.Equal((int)HttpStatusCode.Created, (int)first.StatusCode);
            Assert.Equal((int)HttpStatusCode.Created, (int)second.StatusCode);
            Assert.Equal((int)HttpStatusCode.TooManyRequests, (int)third.StatusCode);
        }
    }
}
