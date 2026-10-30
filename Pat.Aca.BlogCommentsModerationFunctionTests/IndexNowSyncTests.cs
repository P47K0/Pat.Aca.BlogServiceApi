using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class IndexNowSyncTests
    {
        private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private static ArticleDetailChangeFeedDocument MakeArticle(
            string slug, DateTime? publishedAt = null, DateTime? updatedAt = null, bool unlisted = false) => new()
        {
            Slug = slug,
            PublishedAt = (publishedAt ?? Now.AddDays(-30)).ToString("O"),
            UpdatedAt = updatedAt?.ToString("O"),
            Unlisted = unlisted,
        };

        private static (IndexNowSyncProcessor, FakeIndexNowPublisher) Make()
        {
            var publisher = new FakeIndexNowPublisher();
            return (new IndexNowSyncProcessor(publisher, new IndexNowSettings(), NullLogger<IndexNowSyncProcessor>.Instance), publisher);
        }

        [Fact]
        public async Task SyncAsync_pings_a_just_edited_article_with_the_home_page_in_one_request()
        {
            var (processor, publisher) = Make();

            await processor.SyncAsync(new[] { MakeArticle("a", updatedAt: Now.AddMinutes(-2)), MakeArticle("b", publishedAt: Now.AddMinutes(-1)) }, Now);

            var urls = Assert.Single(publisher.Pings);
            Assert.Equal(new[]
            {
                "https://blog.koorevaar.com/",
                "https://blog.koorevaar.com/articles/a",
                "https://blog.koorevaar.com/articles/b",
            }, urls);
        }

        [Fact]
        public async Task SyncAsync_skips_a_view_count_write_with_an_old_updatedAt()
        {
            var (processor, publisher) = Make();

            await processor.SyncAsync(new[] { MakeArticle("a"), MakeArticle("b", updatedAt: Now.AddDays(-2)) }, Now);

            Assert.Empty(publisher.Pings);
        }

        [Fact]
        public async Task SyncAsync_skips_unlisted_future_dated_and_slugless_documents()
        {
            var (processor, publisher) = Make();

            await processor.SyncAsync(new[]
            {
                MakeArticle("about", updatedAt: Now, unlisted: true),
                MakeArticle("later", publishedAt: Now.AddDays(1)),
                MakeArticle("", updatedAt: Now),
            }, Now);

            Assert.Empty(publisher.Pings);
        }

        [Fact]
        public async Task SyncAsync_lists_an_article_once_when_a_batch_holds_it_twice()
        {
            var (processor, publisher) = Make();

            await processor.SyncAsync(new[] { MakeArticle("a", updatedAt: Now), MakeArticle("a", updatedAt: Now) }, Now);

            Assert.Equal(2, Assert.Single(publisher.Pings).Count);
        }

        [Fact]
        public async Task Publisher_posts_host_key_keyLocation_and_urlList()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Accepted, "");
            var publisher = new IndexNowPublisher(new HttpClient(handler), new IndexNowSettings(), NullLogger<IndexNowPublisher>.Instance);

            await publisher.PingAsync(new[] { "https://blog.koorevaar.com/articles/a" });

            Assert.Equal("https://api.indexnow.org/indexnow", handler.LastRequest!.RequestUri!.ToString());
            using var body = JsonDocument.Parse(handler.LastRequestBody!);
            var key = body.RootElement.GetProperty("key").GetString();
            Assert.Equal("blog.koorevaar.com", body.RootElement.GetProperty("host").GetString());
            Assert.Equal($"https://blog.koorevaar.com/{key}.txt", body.RootElement.GetProperty("keyLocation").GetString());
            Assert.Equal("https://blog.koorevaar.com/articles/a", body.RootElement.GetProperty("urlList")[0].GetString());
        }

        [Fact]
        public async Task Publisher_swallows_a_failed_call()
        {
            var publisher = new IndexNowPublisher(
                new HttpClient(new FakeHttpMessageHandler(new HttpRequestException("down"))),
                new IndexNowSettings(), NullLogger<IndexNowPublisher>.Instance);

            await publisher.PingAsync(new[] { "https://blog.koorevaar.com/" });
        }
    }
}
