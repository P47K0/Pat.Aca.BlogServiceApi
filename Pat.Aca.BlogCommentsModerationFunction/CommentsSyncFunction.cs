using Microsoft.Azure.Functions.Worker;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pushes all published comments to api-proxy's KV whenever the
    /// Comments container changes (a new comment, the moderation Function
    /// publishing or unpublishing one, an admin PATCH), so the article page
    /// can show comments without an origin call. Shares CommentsLeases with
    /// CommentModerationFunction under its own LeaseContainerPrefix.
    /// StartFromBeginning makes the first run push the current set right
    /// after deploy instead of waiting for the next comment.
    ///
    /// The daily quota document (CosmosModerationQuotaStore) also lives in
    /// Comments and triggers this; api-proxy skips the write when nothing
    /// changed.
    /// </summary>
    public sealed class CommentsSyncFunction(CommentsSyncProcessor processor)
    {
        [Function("CommentsSyncFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Comments",
                Connection = "CosmosDb",
                LeaseContainerName = "CommentsLeases",
                LeaseContainerPrefix = "CommentsSync",
                StartFromBeginning = true,
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<CommentChangeFeedDocument> changes)
        {
            await processor.SyncAsync();
        }
    }

    /// <summary>
    /// The same comments sync on a timer: an admin DELETE of a comment never
    /// reaches the Change Feed. Every 15 minutes is plenty for a rare manual
    /// action; an unchanged run costs one small Cosmos query and one KV read.
    /// </summary>
    public sealed class CommentsTimerSyncFunction(CommentsSyncProcessor processor)
    {
        [Function("CommentsTimerSyncFunction")]
        public async Task RunAsync([TimerTrigger("0 */15 * * * *")] TimerInfo timer)
        {
            await processor.SyncAsync();
        }
    }
}
