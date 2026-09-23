using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleListRepository(List<ArticleListItem> articles) : IArticleListRepository
    {
        public Task<List<ArticleListItem>> GetLatestArticlesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(articles);
    }
}
