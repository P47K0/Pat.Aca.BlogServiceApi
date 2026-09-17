using Microsoft.Azure.Functions.Worker;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Backs the homepage's "most-viewed post" link (see the backlog item
    /// of that name) with a periodic TimerTrigger rather than a Cosmos DB
    /// Change Feed trigger like ArticleCountSyncFunction -- ViewCount
    /// changes on every article read, not just rare content edits, so
    /// reacting to every write the way the count feature does would run
    /// this query and push to Cloudflare on every single page view
    /// site-wide. Twice a day is a deliberately simple, hardcoded schedule,
    /// same reasoning as CommentSweepFunction's hourly one -- an
    /// operational freshness knob, not something needing day-one tuning.
    ///
    /// No lease container needed (unlike ArticleCountSyncFunction) -- a
    /// TimerTrigger has no Change Feed checkpoint to track.
    /// </summary>
    public sealed class MostViewedSyncFunction(MostViewedSyncProcessor processor)
    {
        [Function("MostViewedSyncFunction")]
        public async Task RunAsync([TimerTrigger("0 0 */12 * * *")] TimerInfo timer)
        {
            await processor.SyncAsync();
        }
    }
}
