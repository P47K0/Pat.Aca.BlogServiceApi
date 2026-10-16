namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes every published comment, grouped by slug, to api-proxy's KV,
    /// so the article page's comments need no origin call. Best-effort like
    /// the other publishers.
    /// </summary>
    public interface IPublishedCommentsPublisher
    {
        Task PublishAsync(IReadOnlyDictionary<string, List<PublishedComment>> commentsBySlug, CancellationToken cancellationToken = default);
    }
}
