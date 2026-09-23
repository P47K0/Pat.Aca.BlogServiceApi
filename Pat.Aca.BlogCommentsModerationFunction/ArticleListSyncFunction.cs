using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The Cosmos DB Change Feed trigger behind the "Blog auto update"
    /// backlog item -- fires on every write to the Articles container
    /// (create, update, an Unlisted toggle) and pushes a freshly-recomputed
    /// latest-articles list into api-proxy's durable KV snapshot via
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
    /// Unlike the count sync, this can't rely on Change Feed deliveries
    /// alone to stay fully correct: CosmosArticleListRepository's query
    /// keeps GetArticlesAsync's future-publishedAt exclusion (this feeds
    /// what readers actually see, not a wall-clock-independent count), so a
    /// scheduled article crossing into "published" with no new Cosmos write
    /// still needs api-proxy's own request-driven revalidate/durable-
    /// fallback machinery to eventually pick it up -- this sync narrows
    /// that staleness window on every real write, it doesn't replace that
    /// machinery entirely.
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
                "Articles Change Feed delivered {Count} change(s), syncing latest-articles list once for the batch.",
                changes.Count);

            await processor.SyncAsync();
        }
    }
}
