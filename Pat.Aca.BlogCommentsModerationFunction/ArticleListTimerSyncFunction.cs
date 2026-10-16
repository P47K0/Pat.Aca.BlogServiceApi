using Microsoft.Azure.Functions.Worker;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The same full-list sync as ArticleListSyncFunction, on a timer
    /// instead of the Change Feed. api-proxy serves /articles from KV only,
    /// so this push is what makes a scheduled article appear once its
    /// publishedAt passes (no Cosmos write happens then) and what drops an
    /// article deleted straight in Cosmos (the Change Feed has no deletes).
    /// It also repairs the list if a Change Feed delivery was missed.
    ///
    /// Every 5 minutes bounds how late a scheduled article shows up. Cheap:
    /// one small Cosmos query, and api-proxy compares the pushed list with
    /// KV and only writes when something changed, so an unchanged run
    /// costs one KV read and no write.
    /// </summary>
    public sealed class ArticleListTimerSyncFunction(ArticleListSyncProcessor processor)
    {
        [Function("ArticleListTimerSyncFunction")]
        public async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo timer)
        {
            await processor.SyncAsync();
        }
    }
}
