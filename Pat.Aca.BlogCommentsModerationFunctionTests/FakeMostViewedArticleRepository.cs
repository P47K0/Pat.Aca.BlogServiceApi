using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeMostViewedArticleRepository(MostViewedArticleResult? result) : IMostViewedArticleRepository
    {
        public Task<MostViewedArticleResult?> GetMostViewedArticleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
