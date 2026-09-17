namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes the current most-viewed article to api-proxy's durable KV
    /// store, backing the homepage's "most-viewed post" link. Same
    /// best-effort posture as IArticleCountPublisher -- a failed call here
    /// just means api-proxy keeps serving whatever it last had (or nothing,
    /// before the first successful sync) until the next twice-daily run.
    /// </summary>
    public interface IMostViewedArticlePublisher
    {
        Task PublishAsync(MostViewedArticleResult article, CancellationToken cancellationToken = default);
    }
}
