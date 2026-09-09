using System.Net;
using Azure.Identity;
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
    /// Connection/auth logic (emulator cert bypass, Gateway mode,
    /// DefaultAzureCredential) is a third copy of the same ~25 lines
    /// already duplicated between CosmosArticleRepository and
    /// CosmosCommentRepository in the sibling API project -- flagged there
    /// as "worth extracting if a third consumer ever needs it", and here
    /// it is. Not extracted in this commit (would mean touching two
    /// already-committed classes as a side effect of adding the quota
    /// store) -- a real candidate for a dedicated simplify pass once the
    /// whole feature is stable, not mid-build.
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
        private const string CommentsContainerId = "Comments";
        private const string QuotaPartitionKeyValue = "__quota__";

        private readonly Container _container;
        private readonly ModerationSettings _moderationSettings;

        public CosmosModerationQuotaStore(CosmosSettings cosmosSettings, ModerationSettings moderationSettings)
        {
            _moderationSettings = moderationSettings;

            var clientOptions = new CosmosClientOptions();
            CosmosClient cosmosClient;

            if (!string.IsNullOrEmpty(cosmosSettings.PrimaryKey))
            {
                var isLocalEmulator = Uri.TryCreate(cosmosSettings.EndpointUri, UriKind.Absolute, out var endpoint)
                    && endpoint.IsLoopback;

                if (isLocalEmulator)
                {
                    clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    });
                    clientOptions.ConnectionMode = ConnectionMode.Gateway;
                }

                cosmosClient = new CosmosClient(cosmosSettings.EndpointUri, cosmosSettings.PrimaryKey, clientOptions);
            }
            else
            {
                // Direct mode needs a wide outbound TCP port range that
                // restricted container networking doesn't reliably support
                // -- same reasoning as the sibling API project's repositories.
                clientOptions.ConnectionMode = ConnectionMode.Gateway;

                cosmosClient = new CosmosClient(
                    accountEndpoint: cosmosSettings.EndpointUri,
                    tokenCredential: new DefaultAzureCredential(),
                    clientOptions: clientOptions);
            }

            _container = cosmosClient.GetDatabase(cosmosSettings.Database).GetContainer(CommentsContainerId);
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
