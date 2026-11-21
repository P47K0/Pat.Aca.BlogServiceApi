using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Cosmos DB Change Feed trigger behind the "IndexNow for the blog"
    /// backlog item: pings IndexNow (Bing and the other participating
    /// engines) when an article is published or edited, so they re-crawl it
    /// within minutes instead of waiting for the sitemap.
    ///
    /// Shares ArticlesLeases under its own LeaseContainerPrefix. No
    /// StartFromBeginning: unlike the KV backfill, a first run must not ping
    /// every existing article. Deletes never reach the Change Feed; a deleted
    /// article is found on the next crawl.
    /// </summary>
    public sealed class IndexNowSyncFunction(
        IndexNowSyncProcessor processor,
        ILogger<IndexNowSyncFunction> logger)
    {
        [Function("IndexNowSyncFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Articles",
                Connection = "CosmosDb",
                LeaseContainerName = "ArticlesLeases",
                LeaseContainerPrefix = "IndexNowSync",
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<ArticleDetailChangeFeedDocument> changes)
        {
            logger.LogInformation("Articles Change Feed delivered {Count} change(s) for the IndexNow ping.", changes.Count);
            await processor.SyncAsync(changes, DateTime.UtcNow);
        }
    }
}
