using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class ArticleCountSyncProcessorTests
    {
        [Fact]
        public async Task SyncAsync_publishes_the_recomputed_count()
        {
            var countRepository = new FakeArticleCountRepository(42);
            var publisher = new FakeArticleCountPublisher();
            var processor = new ArticleCountSyncProcessor(countRepository, publisher, NullLogger<ArticleCountSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Equal(new[] { 42 }, publisher.PublishedCounts);
        }

        [Fact]
        public async Task SyncAsync_publishes_exactly_once_per_call_regardless_of_batch_size()
        {
            // ArticleCountSyncFunction calls SyncAsync once per Change Feed
            // invocation, not once per changed document -- this just proves
            // SyncAsync itself doesn't fan out internally.
            var countRepository = new FakeArticleCountRepository(7);
            var publisher = new FakeArticleCountPublisher();
            var processor = new ArticleCountSyncProcessor(countRepository, publisher, NullLogger<ArticleCountSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Single(publisher.PublishedCounts);
        }
    }
}
