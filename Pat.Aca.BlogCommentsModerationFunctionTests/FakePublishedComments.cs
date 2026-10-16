using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakePublishedCommentsRepository(Dictionary<string, List<PublishedComment>> comments) : IPublishedCommentsRepository
    {
        public Task<Dictionary<string, List<PublishedComment>>> GetPublishedCommentsBySlugAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(comments);
    }

    public sealed class FakePublishedCommentsPublisher : IPublishedCommentsPublisher
    {
        public List<IReadOnlyDictionary<string, List<PublishedComment>>> Published { get; } = new();

        public Task PublishAsync(IReadOnlyDictionary<string, List<PublishedComment>> commentsBySlug, CancellationToken cancellationToken = default)
        {
            Published.Add(commentsBySlug);
            return Task.CompletedTask;
        }
    }
}
