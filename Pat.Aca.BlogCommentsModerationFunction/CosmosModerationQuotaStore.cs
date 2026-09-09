using System.Net;
using Microsoft.Azure.Cosmos;
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
    /// The increment itself is a single atomic, conditional Cosmos Patch
    /// (PatchItemRequestOptions.FilterPredicate) rather than a
    /// read-then-write, so two comments scored at nearly the same moment
    /// can't both slip past the quota by racing a read.
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

        private readonly Container _container;
        private readonly ModerationSettings _moderationSettings;

        public CosmosModerationQuotaStore(Container commentsContainer, ModerationSettings moderationSettings)
        {
            _container = commentsContainer;
            _moderationSettings = moderationSettings;
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
                Count = 1
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
        }
    }
}
