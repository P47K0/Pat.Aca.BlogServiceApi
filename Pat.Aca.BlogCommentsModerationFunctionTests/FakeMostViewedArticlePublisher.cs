using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeMostViewedArticlePublisher : IMostViewedArticlePublisher
    {
        public List<MostViewedArticleResult> PublishedArticles { get; } = new();

        public Task PublishAsync(MostViewedArticleResult article, CancellationToken cancellationToken = default)
        {
            PublishedArticles.Add(article);
            return Task.CompletedTask;
        }
    }
}
