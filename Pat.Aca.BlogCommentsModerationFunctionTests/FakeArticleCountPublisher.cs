using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleCountPublisher : IArticleCountPublisher
    {
        public List<int> PublishedCounts { get; } = new();

        public Task PublishAsync(int count, CancellationToken cancellationToken = default)
        {
            PublishedCounts.Add(count);
            return Task.CompletedTask;
        }
    }
}
