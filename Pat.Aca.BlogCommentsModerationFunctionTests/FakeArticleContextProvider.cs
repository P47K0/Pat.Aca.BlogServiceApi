using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleContextProvider : IArticleContextProvider
    {
        private readonly string? _summary;

        public FakeArticleContextProvider(string? summary = null)
        {
            _summary = summary;
        }

        public string? LastRequestedSlug { get; private set; }

        public Task<string?> GetArticleSummaryAsync(string articleSlug)
        {
            LastRequestedSlug = articleSlug;
            return Task.FromResult(_summary);
        }
    }
}
