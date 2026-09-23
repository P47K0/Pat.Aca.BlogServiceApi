namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes the freshly-recomputed latest-articles list to api-proxy's
    /// durable KV fallback store, so a Cosmos write (create, update, an
    /// Unlisted toggle) is reflected there near-real-time instead of
    /// waiting for the next request-driven revalidate/cold-miss cycle to
    /// notice it -- see the "Blog auto update" backlog item. Same
    /// best-effort posture as IArticleCountPublisher/
    /// IMostViewedArticlePublisher: a failed/unreachable call here never
    /// fails the Change Feed trigger, it just means api-proxy keeps serving
    /// whatever it last had until the next successful sync (the next
    /// article write, or a retried delivery).
    /// </summary>
    public interface IArticleListPublisher
    {
        Task PublishAsync(IReadOnlyList<ArticleListItem> articles, CancellationToken cancellationToken = default);
    }
}
