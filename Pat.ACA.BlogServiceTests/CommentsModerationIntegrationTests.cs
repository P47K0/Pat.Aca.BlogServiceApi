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
    // The AAD-gated Comments.Moderate endpoints (GET .../comments/all,
    // PATCH/DELETE .../comments/{commentId}) — mirrors
    // ApiWriteIntegrationTests.cs's TestAuthHandler-based shape, since these
    // need the same Azure-AD-app-role stand-in as the Articles.Write tests.
    //
    // All tests here share one WebApplicationFactory fixture and therefore
    // one FakeCommentRepository instance (its seed list is not static, but
    // the DI singleton is shared across the class either way) — mutating
    // tests (PATCH/DELETE) are each scoped to their own seeded comment id so
    // they can't interfere with each other, and the read test deliberately
    // asserts specific known entries rather than an exact total count, same
    // "avoid exact counts against shared seed state" precedent as
    // ApiWriteIntegrationTests' own note about FakeArticleRepository.
    public class CommentsModerationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public CommentsModerationIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArticleRepository>();
                    services.AddSingleton<IArticleRepository, FakeArticleRepository>();
                    services.RemoveAll<ICommentRepository>();
                    services.AddSingleton<ICommentRepository, FakeCommentRepository>();

                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                });
            });
        }

        // Same reasoning as ApiWriteIntegrationTests.CreateClient — a unique
        // caller id per client keeps this class's shared
        // ArticlesWriteRateLimiterPolicy budget (reused by the
        // Comments.Moderate endpoints) from one test starving another.
        private HttpClient CreateClient(params string[] roles)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.CallerIdHeaderName, Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeaderName, string.Join(",", roles));
            return client;
        }

        [Fact]
        public async Task GET_comments_all_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/articles/first-article/comments/all");

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_comments_all_without_required_role_returns_403()
        {
            var client = CreateClient("Some.Other.Role");

            var response = await client.GetAsync("/articles/first-article/comments/all");

            Assert.Equal((int)HttpStatusCode.Forbidden, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_comments_all_returns_every_status_including_email()
        {
            var client = CreateClient("Comments.Moderate");

            var response = await client.GetAsync("/articles/first-article/comments/all");

            response.EnsureSuccessStatusCode();
            var comments = await response.Content.ReadFromJsonAsync<List<Comment>>();

            Assert.NotNull(comments);
            // Specific known entries, not an exact total count — see this
            // class's own doc comment on why.
            Assert.Contains(comments!, c => c.Id == "published-comment-1" && c.Status == CommentStatus.Published);
            var unpublished = Assert.Single(comments!, c => c.Id == "unpublished-comment-1");
            Assert.Equal("bob@example.com", unpublished.Email);
            Assert.Equal(2, unpublished.LlmScore);
        }

        [Fact]
        public async Task PATCH_comment_publishes_it()
        {
            var client = CreateClient("Comments.Moderate");

            var response = await client.PatchAsJsonAsync(
                "/articles/first-article/comments/unpublished-comment-1",
                new CommentStatusUpdateRequest(CommentStatus.Published));

            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<Comment>();
            Assert.NotNull(updated);
            Assert.Equal(CommentStatus.Published, updated!.Status);
        }

        [Fact]
        public async Task PATCH_comment_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var response = await client.PatchAsJsonAsync(
                "/articles/first-article/comments/unpublished-comment-1",
                new CommentStatusUpdateRequest(CommentStatus.Published));

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task PATCH_comment_rejects_an_invalid_status_value()
        {
            var client = CreateClient("Comments.Moderate");

            var response = await client.PatchAsJsonAsync(
                "/articles/first-article/comments/published-comment-1",
                new CommentStatusUpdateRequest("not-a-real-status"));

            Assert.Equal((int)HttpStatusCode.BadRequest, (int)response.StatusCode);
        }

        [Fact]
        public async Task PATCH_comment_returns_404_for_unknown_comment_id()
        {
            var client = CreateClient("Comments.Moderate");

            var response = await client.PatchAsJsonAsync(
                "/articles/first-article/comments/does-not-exist",
                new CommentStatusUpdateRequest(CommentStatus.Published));

            Assert.Equal((int)HttpStatusCode.NotFound, (int)response.StatusCode);
        }

        [Fact]
        public async Task DELETE_comment_removes_it()
        {
            var client = CreateClient("Comments.Moderate");

            using var deleteResponse = await client.DeleteAsync("/articles/first-article/comments/queued-comment-1");
            Assert.Equal((int)HttpStatusCode.NoContent, (int)deleteResponse.StatusCode);

            var getAllResponse = await client.GetAsync("/articles/first-article/comments/all");
            var remaining = await getAllResponse.Content.ReadFromJsonAsync<List<Comment>>();
            Assert.DoesNotContain(remaining!, c => c.Id == "queued-comment-1");
        }

        [Fact]
        public async Task DELETE_comment_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var response = await client.DeleteAsync("/articles/first-article/comments/queued-comment-1");

            Assert.Equal((int)HttpStatusCode.Unauthorized, (int)response.StatusCode);
        }

        [Fact]
        public async Task DELETE_comment_returns_404_for_unknown_comment_id()
        {
            var client = CreateClient("Comments.Moderate");

            var response = await client.DeleteAsync("/articles/first-article/comments/does-not-exist");

            Assert.Equal((int)HttpStatusCode.NotFound, (int)response.StatusCode);
        }
    }
}
