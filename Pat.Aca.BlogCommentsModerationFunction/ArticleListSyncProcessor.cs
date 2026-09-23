using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pure orchestration behind ArticleListSyncFunction, decoupled from the
    /// Cosmos DB trigger binding for testability -- same split as
    /// ArticleCountSyncProcessor vs. ArticleCountSyncFunction. Recompute,
    /// then publish unconditionally -- deduping an unchanged list against
    /// what's already in KV is api-proxy's own job (mirrors
    /// writeFallbackSnapshot's existing articleContentEquals check), not
    /// this processor's, so a quiet batch (e.g. a viewCount-only write that
    /// still surfaces on the Change Feed) costs one extra KV read on the
    /// api-proxy side, not a skipped sync here.
    /// </summary>
    public sealed class ArticleListSyncProcessor(
        IArticleListRepository listRepository,
        IArticleListPublisher listPublisher,
        ILogger<ArticleListSyncProcessor> logger)
    {
        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            var articles = await listRepository.GetLatestArticlesAsync(cancellationToken);
            logger.LogInformation("Recomputed latest {Count} article(s). Publishing to api-proxy.", articles.Count);
            await listPublisher.PublishAsync(articles, cancellationToken);
        }
    }
}
