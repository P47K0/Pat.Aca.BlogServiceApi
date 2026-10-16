using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pure orchestration behind ArticleDetailSyncFunction, decoupled from
    /// the trigger binding for testability (same split as
    /// ArticleListSyncProcessor). One push per changed article, unlike the
    /// list sync's one push per batch: every article has its own KV key.
    /// A batch can hold the same article more than once; only its last
    /// version is pushed. Skipping an unchanged article is api-proxy's job
    /// (it compares with what KV already holds), as for the list.
    /// </summary>
    public sealed class ArticleDetailSyncProcessor(
        IArticleDetailPublisher publisher,
        ILogger<ArticleDetailSyncProcessor> logger)
    {
        public async Task SyncAsync(IReadOnlyList<ArticleDetailChangeFeedDocument> changes, CancellationToken cancellationToken = default)
        {
            var latestBySlug = new Dictionary<string, ArticleDetailChangeFeedDocument>();
            foreach (var change in changes)
            {
                if (string.IsNullOrEmpty(change.Slug))
                {
                    continue;
                }
                latestBySlug[change.Slug] = change;
            }

            logger.LogInformation(
                "Pushing {Count} changed article(s) to api-proxy.", latestBySlug.Count);

            // Sequential: each push renders Markdown in api-proxy, and the
            // first run's backfill delivers every article at once.
            foreach (var article in latestBySlug.Values)
            {
                await publisher.PublishAsync(article, cancellationToken);
            }
        }
    }
}
