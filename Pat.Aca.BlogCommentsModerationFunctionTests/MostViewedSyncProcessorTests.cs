using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class MostViewedSyncProcessorTests
    {
        [Fact]
        public async Task SyncAsync_publishes_the_most_viewed_article_when_one_exists()
        {
            var article = new MostViewedArticleResult("popular-post", "Popular Post", "A summary.", 42, "https://images.koorevaar.com/covers/popular-post.jpg");
            var repository = new FakeMostViewedArticleRepository(article);
            var publisher = new FakeMostViewedArticlePublisher();
            var processor = new MostViewedSyncProcessor(repository, publisher, NullLogger<MostViewedSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Equal(new[] { article }, publisher.PublishedArticles);
        }

        [Fact]
        public async Task SyncAsync_does_not_publish_when_no_eligible_article_exists()
        {
            var repository = new FakeMostViewedArticleRepository(null);
            var publisher = new FakeMostViewedArticlePublisher();
            var processor = new MostViewedSyncProcessor(repository, publisher, NullLogger<MostViewedSyncProcessor>.Instance);

            await processor.SyncAsync();

            Assert.Empty(publisher.PublishedArticles);
        }
    }
}
