using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The Cosmos DB Change Feed trigger behind the "Blog auto update"
    /// backlog item -- fires on every write to the Articles container
    /// (create, update, an Unlisted toggle) and pushes a freshly-recomputed
    /// full article list into api-proxy's durable KV snapshot via
    /// ArticleListSyncProcessor, near-real-time rather than waiting for the
    /// next request-driven revalidate/cold-miss to notice the write.
    ///
    /// Same "recompute the whole thing, don't reason about deltas" choice
    /// as ArticleCountSyncFunction, and the same batch-collapsing (one sync
    /// per invocation regardless of how many documents changed together) --
    /// see that class's own doc comment for the shared reasoning.
    ///
    /// Shares ArticleCountSyncFunction's own ArticlesLeases lease container
    /// rather than provisioning a second one -- LeaseContainerPrefix keeps
    /// the two Change Feed processors' checkpoints from colliding within
    /// it, and the Function's managed identity already has Data Contributor
    /// on the whole container from that feature, so no new RBAC is needed
    /// either.
    ///
    /// Unlike the count sync, Change Feed deliveries alone can't keep this
    /// fully correct: CosmosArticleListRepository's query keeps
    /// GetArticlesAsync's future-publishedAt exclusion, so a scheduled
    /// article crossing into "published" causes no Cosmos write, and a
    /// direct Cosmos delete never reaches the Change Feed at all. Since
    /// api-proxy serves /articles from this push alone (no origin call),
    /// ArticleListTimerSyncFunction runs the same sync on a timer to cover
    /// both.
    /// </summary>
    public sealed class ArticleListSyncFunction(
        ArticleListSyncProcessor processor,
        ILogger<ArticleListSyncFunction> logger)
    {
        [Function("ArticleListSyncFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Articles",
                Connection = "CosmosDb",
                LeaseContainerName = "ArticlesLeases",
                LeaseContainerPrefix = "ArticleListSync",
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<ArticleChangeFeedDocument> changes)
        {
            logger.LogInformation(
                "Articles Change Feed delivered {Count} change(s), syncing full article list once for the batch.",
                changes.Count);

            await processor.SyncAsync();
        }
    }
}
