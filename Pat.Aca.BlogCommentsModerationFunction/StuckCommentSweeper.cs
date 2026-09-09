using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Recovers comments that CommentModerationFunction deliberately left
    /// at Queued without retrying -- a scoring failure (Cloudflare down)
    /// or the daily quota being exhausted (see that class's own doc
    /// comment on why it doesn't self-touch a document to force an
    /// immediate retry: it would create a tight loop for as long as the
    /// underlying cause persists). Run periodically (see
    /// CommentSweepFunction's Timer trigger) rather than immediately, this
    /// is that same idea done safely: find comments still Queued after a
    /// grace period, and apply a real (if value-identical) Cosmos write to
    /// each -- any successful write bumps a document's _etag/_ts, which is
    /// what actually drives Change Feed delivery, so this reliably queues
    /// each one up for another CommentModerationFunction attempt without
    /// needing to know or care *why* it didn't complete the first time.
    ///
    /// Uses the same shared Comments Container as
    /// CosmosModerationQuotaStore/CommentStatusWriter (built once in
    /// Program.cs) -- see either class's own doc comment for why.
    /// </summary>
    public sealed class StuckCommentSweeper
    {
        // Comments younger than this are left alone -- a brand new comment
        // is expected to still be Queued for the first few moments before
        // CommentModerationFunction's own Change Feed delivery reaches it;
        // sweeping it this early would just be noise, not a real recovery.
        private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

        private readonly Container _container;

        public StuckCommentSweeper(Container commentsContainer)
        {
            _container = commentsContainer;
        }

        /// <summary>
        /// Finds and re-queues every stuck comment. Returns how many were
        /// touched, purely so the caller can log something useful only
        /// when there's actually something to report.
        /// </summary>
        public async Task<int> SweepAsync()
        {
            var cutoff = DateTime.UtcNow - GracePeriod;
            var query = new QueryDefinition(
                "SELECT c.id, c.articleSlug FROM c WHERE c.status = @status AND c.createdAt < @cutoff")
                .WithParameter("@status", ModerationCommentStatus.Queued)
                .WithParameter("@cutoff", cutoff);

            using FeedIterator<StuckCommentRef> iterator = _container.GetItemQueryIterator<StuckCommentRef>(query);
            var touchedCount = 0;

            while (iterator.HasMoreResults)
            {
                FeedResponse<StuckCommentRef> response = await iterator.ReadNextAsync();
                foreach (var stuck in response.Resource)
                {
                    await _container.PatchItemAsync<object>(
                        stuck.Id,
                        new PartitionKey(stuck.ArticleSlug),
                        new[] { PatchOperation.Set("/status", ModerationCommentStatus.Queued) });
                    touchedCount++;
                }
            }

            return touchedCount;
        }

        // A direct SDK query/patch, not a trigger binding -- uses
        // Newtonsoft's [JsonProperty] like every other class in this
        // Function that talks to Cosmos directly (CosmosModerationQuotaStore,
        // CommentStatusWriter), not System.Text.Json like
        // CommentChangeFeedDocument -- that one's different specifically
        // because the Cosmos DB *trigger extension* deserializes its own
        // payloads via System.Text.Json regardless of this Function's own
        // CosmosClient serializer (which defaults to Newtonsoft, same as
        // the sibling API project's).
        private sealed class StuckCommentRef
        {
            [JsonProperty("id")]
            public string Id { get; set; } = "";

            [JsonProperty("articleSlug")]
            public string ArticleSlug { get; set; } = "";
        }
    }
}
