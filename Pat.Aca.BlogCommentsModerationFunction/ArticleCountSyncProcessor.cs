using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pure orchestration behind ArticleCountSyncFunction, decoupled from
    /// the Cosmos DB trigger binding so it's unit-testable with fakes --
    /// same split as CommentModerationProcessor vs. CommentModerationFunction.
    /// Recompute, then publish; no retry/backoff here -- IArticleCountPublisher
    /// is already best-effort by design, and the next real article write
    /// (or, once implemented, a retried Change Feed delivery) is the natural
    /// retry for a failed sync, same reasoning as CommentModerationFunction
    /// deliberately not force-retrying a failed scoring call.
    /// </summary>
    public sealed class ArticleCountSyncProcessor(
        IArticleCountRepository countRepository,
        IArticleCountPublisher countPublisher,
        ILogger<ArticleCountSyncProcessor> logger)
    {
        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            var count = await countRepository.GetArticleCountAsync(cancellationToken);
            logger.LogInformation("Recomputed blog-post count: {Count}. Publishing to api-proxy.", count);
            await countPublisher.PublishAsync(count, cancellationToken);
        }
    }
}
