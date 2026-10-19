namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes one article's full record to api-proxy's per-slug KV snapshot
    /// (rendered HTML for the article page, raw Markdown for .md). Same
    /// best-effort posture as IArticleListPublisher: a failed call never
    /// fails the Change Feed delivery.
    /// </summary>
    public interface IArticleDetailPublisher
    {
        Task PublishAsync(ArticleDetailChangeFeedDocument article, CancellationToken cancellationToken = default);
    }
}
