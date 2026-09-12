using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IModerationQuotaStore implementation. Reuses the Comments
    /// Cosmos container (no new container/resource) by storing one small
    /// synthetic counter document per UTC day, distinguished from real
    /// comments by a fixed id pattern and a sentinel partition-key value --
    /// see QuotaDocument's own doc comment for why it still needs a real
    /// "articleSlug" field despite not being a comment.
    ///
    /// A date-scoped document id (moderation-quota-yyyy-MM-dd) means no
    /// explicit "is it a new day yet" reset logic is ever needed -- each
    /// day just gets a fresh id, created lazily on that day's first call.
    /// Each document also gets a TTL (QuotaDocumentTtlSeconds) so old days'
    /// documents self-expire instead of accumulating forever -- found in
    /// production 2026-09-11 that nothing had ever cleaned these up.
    /// The increment itself is a single atomic, conditional Cosmos Patch
    /// (PatchItemRequestOptions.FilterPredicate) rather than a
    /// read-then-write, so two comments scored at nearly the same moment
    /// can't both slip past the quota by racing a read. ReleaseAsync is the
    /// same idea running backwards (a conditional decrement, floored at 0)
    /// -- CommentModerationProcessor calls it to give back a slot when the
    /// scoring attempt it was reserved for fails, so a run of infra
    /// failures doesn't silently burn the whole day's quota on comments
    /// that never actually got moderated.
    ///
    /// Takes the Comments Container directly (built once in Program.cs and
    /// shared with CommentStatusWriter) rather than building its own
    /// CosmosClient -- the connection/auth bootstrap (emulator cert
    /// bypass, Gateway mode, DefaultAzureCredential) was originally
    /// duplicated here as a third copy of the same ~25 lines already in
    /// CosmosArticleRepository/CosmosCommentRepository in the sibling API
    /// project; consolidated to one construction site within this project
    /// once a second consumer (CommentStatusWriter) needed the same
    /// Container, rather than repeating it a fourth time. A cross-project
    /// shared library covering the API project's own two copies too was
    /// explicitly discussed and deferred -- this is a smaller,
    /// same-project-only tidy-up, not that larger extraction.
    ///
    /// Not directly unit tested, consistent with this project's existing
    /// practice for its other real Cosmos-calling classes
    /// (CosmosArticleRepository/CosmosCommentRepository are exercised only
    /// through Fake-repository-backed endpoint tests, never against a real
    /// or emulated Cosmos instance in the automated suite) -- correctness
    /// here rests on matching Cosmos's documented Patch/conditional-filter
    /// API surface plus a successful build, not a live functional test;
    /// worth a manual Cosmos DB Emulator smoke test before this ships.
    /// </summary>
    public sealed class CosmosModerationQuotaStore : IModerationQuotaStore
    {
        private const string QuotaPartitionKeyValue = "__quota__";

        // Retention for a day's quota document once it's no longer today's --
        // the document is only ever relevant for the one UTC day it counts,
        // so 2 days is just enough buffer to still see yesterday's count
        // while troubleshooting, without letting them accumulate forever.
        // Relies on the Comments container's defaultTtl: -1
        // (infra/cosmos-db.bicep), which enables per-item TTL without
        // forcing any expiration on real comment documents, which never set
        // this field at all.
        private const int QuotaDocumentTtlSeconds = 2 * 24 * 60 * 60;

        private readonly Container _container;
        private readonly ModerationSettings _moderationSettings;
        private readonly ILogger<CosmosModerationQuotaStore> _logger;

        public CosmosModerationQuotaStore(
            Container commentsContainer,
            ModerationSettings moderationSettings,
            ILogger<CosmosModerationQuotaStore> logger)
        {
            _container = commentsContainer;
            _moderationSettings = moderationSettings;
            _logger = logger;
        }

        public async Task<bool> TryConsumeAsync()
        {
            var docId = TodaysQuotaDocumentId();
            var partitionKey = new PartitionKey(QuotaPartitionKeyValue);

            try
            {
                await IncrementIfUnderQuotaAsync(docId, partitionKey);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Today's quota is exhausted.
                return false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // First call of a new UTC day -- today's document doesn't exist yet.
                return await TryCreateTodaysDocumentAsync(docId, partitionKey);
            }
        }

        public async Task ReleaseAsync()
        {
            var docId = TodaysQuotaDocumentId();
            var partitionKey = new PartitionKey(QuotaPartitionKeyValue);

            try
            {
                await _container.PatchItemAsync<QuotaDocument>(
                    docId,
                    partitionKey,
                    new[] { PatchOperation.Increment("/count", -1) },
                    new PatchItemRequestOptions
                    {
                        FilterPredicate = "FROM c WHERE c.count > 0"
                    });
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
            {
                // Nothing sensible to release: either today's document
                // doesn't exist yet (shouldn't happen -- TryConsumeAsync
                // always creates it first) or count is already 0 (a UTC day
                // boundary crossed between the consume and this release).
                // Either way this is a best-effort compensating action, not
                // a correctness-critical one -- see ReleaseAsync's own doc
                // comment on IModerationQuotaStore.
                _logger.LogWarning(
                    ex,
                    "Could not release a quota slot for {DocId} -- leaving today's count as-is.",
                    docId);
            }
        }

        private Task IncrementIfUnderQuotaAsync(string docId, PartitionKey partitionKey) =>
            _container.PatchItemAsync<QuotaDocument>(
                docId,
                partitionKey,
                new[] { PatchOperation.Increment("/count", 1) },
                new PatchItemRequestOptions
                {
                    FilterPredicate = $"FROM c WHERE c.count < {_moderationSettings.DailyModerationQuota}"
                });

        private async Task<bool> TryCreateTodaysDocumentAsync(string docId, PartitionKey partitionKey)
        {
            var document = new QuotaDocument
            {
                Id = docId,
                ArticleSlug = QuotaPartitionKeyValue,
                Count = 1,
                Ttl = QuotaDocumentTtlSeconds
            };

            try
            {
                await _container.CreateItemAsync(document, partitionKey);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Lost the race to create today's document to a concurrent
                // invocation -- it exists now, retry the conditional increment.
                try
                {
                    await IncrementIfUnderQuotaAsync(docId, partitionKey);
                    return true;
                }
                catch (CosmosException retryEx) when (retryEx.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    return false;
                }
            }
        }

        private static string TodaysQuotaDocumentId() => $"moderation-quota-{DateTime.UtcNow:yyyy-MM-dd}";

        // Must carry a real "articleSlug" field despite not being a comment
        // -- the Comments container's declared partition key path (see
        // infra/cosmos-db.bicep) is /articleSlug, so every document stored
        // here needs it populated. Holds QuotaPartitionKeyValue, never a
        // real article slug.
        private sealed class QuotaDocument
        {
            [JsonProperty("id")]
            public string Id { get; set; } = "";

            [JsonProperty("articleSlug")]
            public string ArticleSlug { get; set; } = "";

            [JsonProperty("count")]
            public int Count { get; set; }

            // Cosmos's reserved per-item TTL field (seconds since last
            // modified). Only set here, never on a real Comment document --
            // see QuotaDocumentTtlSeconds's own doc comment.
            [JsonProperty("ttl")]
            public int? Ttl { get; set; }
        }
    }
}
