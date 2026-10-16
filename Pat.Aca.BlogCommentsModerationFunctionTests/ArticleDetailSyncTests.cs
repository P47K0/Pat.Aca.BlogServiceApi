using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class ArticleDetailSyncTests
    {
        private static ArticleDetailChangeFeedDocument MakeArticle(string slug, string title = "Title") => new()
        {
            Slug = slug,
            Title = title,
            Summary = "Summary",
            Content = "# Title\n\nBody",
            PublishedAt = "2026-10-16T02:18:23Z",
            Tags = new() { "ai" },
        };

        [Fact]
        public async Task SyncAsync_pushes_each_changed_article_once()
        {
            var publisher = new FakeArticleDetailPublisher();
            var processor = new ArticleDetailSyncProcessor(publisher, NullLogger<ArticleDetailSyncProcessor>.Instance);

            await processor.SyncAsync(new[] { MakeArticle("a"), MakeArticle("b") });

            Assert.Equal(new[] { "a", "b" }, publisher.Published.Select(article => article.Slug));
        }

        [Fact]
        public async Task SyncAsync_pushes_only_the_last_version_of_an_article_in_one_batch()
        {
            var publisher = new FakeArticleDetailPublisher();
            var processor = new ArticleDetailSyncProcessor(publisher, NullLogger<ArticleDetailSyncProcessor>.Instance);

            await processor.SyncAsync(new[] { MakeArticle("a", "First"), MakeArticle("a", "Second") });

            var published = Assert.Single(publisher.Published);
            Assert.Equal("Second", published.Title);
        }

        [Fact]
        public async Task SyncAsync_skips_documents_without_a_slug()
        {
            var publisher = new FakeArticleDetailPublisher();
            var processor = new ArticleDetailSyncProcessor(publisher, NullLogger<ArticleDetailSyncProcessor>.Instance);

            await processor.SyncAsync(new[] { MakeArticle("") });

            Assert.Empty(publisher.Published);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Change_feed_document_deserializes_a_stored_article_and_ignores_the_legacy_Id(bool caseInsensitive)
        {
            // The isolated-worker trigger deserializes with System.Text.Json;
            // a legacy document carries both the Cosmos "id" and an int "Id".
            const string json = """
                {"id":"6f1c","Id":12,"slug":"a","title":"T","summary":"S","content":"# T","publishedAt":"2026-10-16T02:18:23Z",
                 "tags":["ai"],"viewCount":5,"seriesName":"S","seriesOrder":2,"relatedSlugs":["b"],"unlisted":true,
                 "updatedAt":"2026-10-15T17:23:37.6624072Z","_ts":1}
                """;

            // The Functions worker's default options are case-insensitive.
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = caseInsensitive };
            var document = JsonSerializer.Deserialize<ArticleDetailChangeFeedDocument>(json, options)!;

            Assert.Equal("a", document.Slug);
            Assert.Equal(2, document.SeriesOrder);
            Assert.True(document.Unlisted);
            Assert.Equal("2026-10-15T17:23:37.6624072Z", document.UpdatedAt);
        }

        [Fact]
        public async Task Publisher_posts_camelCase_fields_to_the_detail_endpoint_on_the_list_url_host()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.NoContent, "");
            var settings = new ApiProxySettings
            {
                SetArticleListUrl = "https://proxy.example/internal/article-list",
                ArticleCountSyncSecret = "secret",
            };
            var publisher = new ApiProxyArticleDetailPublisher(
                new HttpClient(handler), settings, NullLogger<ApiProxyArticleDetailPublisher>.Instance);

            await publisher.PublishAsync(MakeArticle("a"));

            Assert.Equal("https://proxy.example/internal/article-detail", handler.LastRequest!.RequestUri!.ToString());
            Assert.Equal("secret", handler.LastRequest.Headers.GetValues("X-Article-Count-Sync-Key").Single());
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            Assert.Equal("a", body.RootElement.GetProperty("slug").GetString());
            Assert.Equal("# Title\n\nBody", body.RootElement.GetProperty("content").GetString());
            Assert.False(body.RootElement.GetProperty("unlisted").GetBoolean());
        }
    }
}
