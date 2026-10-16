using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class CommentsSyncTests
    {
        private static readonly PublishedComment Comment =
            new("c1", "Ann", "Nice post", new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc));

        [Fact]
        public async Task SyncAsync_publishes_the_recomputed_comments()
        {
            var comments = new Dictionary<string, List<PublishedComment>> { ["a"] = new() { Comment } };
            var publisher = new FakePublishedCommentsPublisher();
            var processor = new CommentsSyncProcessor(
                new FakePublishedCommentsRepository(comments), publisher, NullLogger<CommentsSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Same(comments, Assert.Single(publisher.Published));
        }

        [Fact]
        public async Task SyncAsync_publishes_an_empty_set_so_a_last_deleted_comment_disappears()
        {
            var publisher = new FakePublishedCommentsPublisher();
            var processor = new CommentsSyncProcessor(
                new FakePublishedCommentsRepository(new()), publisher, NullLogger<CommentsSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Empty(Assert.Single(publisher.Published));
        }

        [Fact]
        public async Task Publisher_posts_comments_grouped_by_slug_without_private_fields()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NoContent, "");
            var settings = new ApiProxySettings { SetArticleListUrl = "https://proxy.example/internal/article-list" };
            var publisher = new ApiProxyPublishedCommentsPublisher(
                new HttpClient(handler), settings, NullLogger<ApiProxyPublishedCommentsPublisher>.Instance);

            await publisher.PublishAsync(new Dictionary<string, List<PublishedComment>> { ["a"] = new() { Comment } });

            Assert.Equal("https://proxy.example/internal/comments", handler.LastRequest!.RequestUri!.ToString());
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            var comment = body.RootElement.GetProperty("comments").GetProperty("a")[0];
            Assert.Equal("c1", comment.GetProperty("id").GetString());
            Assert.Equal("2026-10-16T08:00:00Z", comment.GetProperty("createdAt").GetString());
            Assert.Equal(4, comment.EnumerateObject().Count());
        }
    }
}
