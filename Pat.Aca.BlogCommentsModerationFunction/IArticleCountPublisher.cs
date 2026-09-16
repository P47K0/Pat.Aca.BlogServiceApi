namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes a freshly-recomputed blog-post count to api-proxy's durable
    /// KV fallback store, so the site's blog-post counter can be a pure KV
    /// read with no ACA dependency (see the "blog-post counter" backlog
    /// item) -- structurally eliminating the cold-start risk, not just
    /// racing/falling back to it the way the article-list/detail KV
    /// fallbacks do.
    ///
    /// Deliberately best-effort, same posture as IAssistantCacheInvalidator
    /// in the sibling API project: a failed/unreachable call here never
    /// fails the Change Feed trigger itself, it just means api-proxy serves
    /// a stale count until the next successful sync (the next article
    /// write, or -- once that lands -- a retried delivery).
    /// </summary>
    public interface IArticleCountPublisher
    {
        Task PublishAsync(int count, CancellationToken cancellationToken = default);
    }
}
