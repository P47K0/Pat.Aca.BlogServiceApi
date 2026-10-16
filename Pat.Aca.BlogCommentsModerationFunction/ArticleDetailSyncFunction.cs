using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Cosmos DB Change Feed trigger behind the "Extend Change-Feed push to
    /// individual article detail pages" backlog item: pushes every changed
    /// article to api-proxy's `article:{slug}` (and `.md`) KV snapshots, so
    /// the article page can be served without an origin call.
    ///
    /// Shares ArticlesLeases with the count and list syncs under its own
    /// LeaseContainerPrefix. StartFromBeginning only applies while this
    /// prefix has no leases yet: the first run reads the whole container
    /// (the latest version of every article), which backfills a snapshot for
    /// every article, including ones no reader has opened. After that it
    /// continues from its checkpoint like the other triggers.
    ///
    /// No Unlisted/publishedAt filter: unlisted articles (the about page)
    /// are read by slug too, and api-proxy hides a future-dated article at
    /// read time, so a scheduled article needs no extra push when it goes
    /// live. Deletes never reach the Change Feed; there is no delete
    /// endpoint today.
    /// </summary>
    public sealed class ArticleDetailSyncFunction(
        ArticleDetailSyncProcessor processor,
        ILogger<ArticleDetailSyncFunction> logger)
    {
        [Function("ArticleDetailSyncFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Articles",
                Connection = "CosmosDb",
                LeaseContainerName = "ArticlesLeases",
                LeaseContainerPrefix = "ArticleDetailSync",
                StartFromBeginning = true,
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<ArticleDetailChangeFeedDocument> changes)
        {
            logger.LogInformation("Articles Change Feed delivered {Count} change(s) for the detail sync.", changes.Count);
            await processor.SyncAsync(changes);
        }
    }
}
