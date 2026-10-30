using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeIndexNowPublisher : IIndexNowPublisher
    {
        public List<IReadOnlyList<string>> Pings { get; } = new();

        public Task PingAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken = default)
        {
            Pings.Add(urls);
            return Task.CompletedTask;
        }
    }
}
