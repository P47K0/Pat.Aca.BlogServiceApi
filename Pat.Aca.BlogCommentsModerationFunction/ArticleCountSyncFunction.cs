using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The Cosmos DB Change Feed trigger backing the site's blog-post
    /// counter (see the "blog-post counter" backlog item) -- fires on every
    /// write to the Articles container (create, update, an Unlisted
    /// toggle) and recomputes the whole-container count via
    /// ArticleCountSyncProcessor rather than reasoning about individual
    /// deltas, the same "recompute, don't increment" choice made for this
    /// item up front: self-healing against missed/duplicate/out-of-order
    /// deliveries, at the cost of one extra Cosmos RU query per write --
    /// negligible at this project's traffic.
    ///
    /// A batch can carry multiple changed documents at once (e.g. several
    /// articles written close together) -- deliberately synced ONCE per
    /// invocation regardless of batch size, not once per document, since
    /// the recompute query already reflects the whole container's current
    /// state; syncing per-document would just be redundant identical work.
    ///
    /// No periodic TimerTrigger backstop needed here (unlike
    /// CommentSweepFunction for stuck comments) -- IArticleRepository.
    /// GetArticleCountAsync deliberately has no future-publishedAt filter
    /// specifically so this count only ever changes on an actual Cosmos
    /// write, never on wall-clock time alone, which is what makes a purely
    /// Change-Feed-driven sync sufficient on its own.
    ///
    /// Same identity-based Connection/lease-container binding convention as
    /// CommentModerationFunction -- see that class's own doc comment for
    /// the full mechanics (this is also the one piece of this feature that
    /// most needs a real deployment to confirm, not just a clean build).
    /// </summary>
    public sealed class ArticleCountSyncFunction(
        ArticleCountSyncProcessor processor,
        ILogger<ArticleCountSyncFunction> logger)
    {
        [Function("ArticleCountSyncFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Articles",
                Connection = "CosmosDb",
                LeaseContainerName = "ArticlesLeases",
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<ArticleChangeFeedDocument> changes)
        {
            logger.LogInformation(
                "Articles Change Feed delivered {Count} change(s), syncing blog-post count once for the batch.",
                changes.Count);

            await processor.SyncAsync();
        }
    }
}
