using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleDetailPublisher : IArticleDetailPublisher
    {
        public List<ArticleDetailChangeFeedDocument> Published { get; } = new();

        public Task PublishAsync(ArticleDetailChangeFeedDocument article, CancellationToken cancellationToken = default)
        {
            Published.Add(article);
            return Task.CompletedTask;
        }
    }
}
