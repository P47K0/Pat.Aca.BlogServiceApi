using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class ArticleListSyncProcessorTests
    {
        private static ArticleListItem MakeArticle(string slug) =>
            new(1, slug, "Title", "Summary", "2026-01-01T00:00:00Z", new[] { "tag" }, 0, null);

        [Fact]
        public async Task SyncAsync_publishes_the_recomputed_list()
        {
            var articles = new List<ArticleListItem> { MakeArticle("a"), MakeArticle("b") };
            var listRepository = new FakeArticleListRepository(articles);
            var publisher = new FakeArticleListPublisher();
            var processor = new ArticleListSyncProcessor(listRepository, publisher, NullLogger<ArticleListSyncProcessor>.Instance);

            await processor.SyncAsync();

            var published = Assert.Single(publisher.PublishedBatches);
            Assert.Equal(articles, published);
        }

        [Fact]
        public async Task SyncAsync_publishes_exactly_once_per_call_regardless_of_batch_size()
        {
            // ArticleListSyncFunction calls SyncAsync once per Change Feed
            // invocation, not once per changed document -- this just proves
            // SyncAsync itself doesn't fan out internally.
            var listRepository = new FakeArticleListRepository(new List<ArticleListItem> { MakeArticle("a") });
            var publisher = new FakeArticleListPublisher();
            var processor = new ArticleListSyncProcessor(listRepository, publisher, NullLogger<ArticleListSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Single(publisher.PublishedBatches);
        }

        [Fact]
        public async Task SyncAsync_publishes_an_empty_list_when_nothing_is_eligible()
        {
            // Unlike MostViewedSyncProcessor's "skip when nothing eligible"
            // choice, an empty list is itself a valid, meaningful state here
            // (a brand-new blog with zero published posts) -- api-proxy's
            // own snapshot shape already handles an empty articles array,
            // so there's no reason to special-case skipping the publish.
            var listRepository = new FakeArticleListRepository(new List<ArticleListItem>());
            var publisher = new FakeArticleListPublisher();
            var processor = new ArticleListSyncProcessor(listRepository, publisher, NullLogger<ArticleListSyncProcessor>.Instance);

            await processor.SyncAsync();

            var published = Assert.Single(publisher.PublishedBatches);
            Assert.Empty(published);
        }
    }
}
