using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleCountRepository(int count) : IArticleCountRepository
    {
        public Task<int> GetArticleCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(count);
    }
}
