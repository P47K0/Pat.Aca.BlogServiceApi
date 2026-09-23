using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleListPublisher : IArticleListPublisher
    {
        public List<IReadOnlyList<ArticleListItem>> PublishedBatches { get; } = new();

        public Task PublishAsync(IReadOnlyList<ArticleListItem> articles, CancellationToken cancellationToken = default)
        {
            PublishedBatches.Add(articles);
            return Task.CompletedTask;
        }
    }
}
