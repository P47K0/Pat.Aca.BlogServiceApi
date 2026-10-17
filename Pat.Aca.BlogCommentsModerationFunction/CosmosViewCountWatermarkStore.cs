using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Keeps the view-count watermark as one synthetic document in the
    /// Comments container, under its own sentinel partition key, the same way
    /// CosmosModerationQuotaStore keeps its daily quota documents there (no
    /// new container, and the Function can already write that one). It never
    /// has a status, so CommentModerationFunction skips it on the Change
    /// Feed. Not unit tested, like this project's other Cosmos classes.
    /// </summary>
    public sealed class CosmosViewCountWatermarkStore(Container commentsContainer) : IViewCountWatermarkStore
    {
        private const string PartitionKeyValue = "__view-counts__";
        private const string DocumentId = "view-count-watermark";

        private static readonly PartitionKey Partition = new(PartitionKeyValue);

        public async Task<ViewCountWatermark?> GetAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await commentsContainer.ReadItemAsync<WatermarkDocument>(DocumentId, Partition, cancellationToken: cancellationToken);
                return new ViewCountWatermark(
                    DateTime.SpecifyKind(response.Resource.CountedUntil, DateTimeKind.Utc),
                    response.ETag);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> TryCreateAsync(DateTime countedUntil, CancellationToken cancellationToken = default)
        {
            try
            {
                await commentsContainer.CreateItemAsync(NewDocument(countedUntil), Partition, cancellationToken: cancellationToken);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                return false;
            }
        }

        public async Task<bool> TryAdvanceAsync(ViewCountWatermark current, DateTime countedUntil, CancellationToken cancellationToken = default)
        {
            try
            {
                await commentsContainer.ReplaceItemAsync(
                    NewDocument(countedUntil),
                    DocumentId,
                    Partition,
                    new ItemRequestOptions { IfMatchEtag = current.ETag },
                    cancellationToken);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return false;
            }
        }

        private static WatermarkDocument NewDocument(DateTime countedUntil) => new()
        {
            Id = DocumentId,
            ArticleSlug = PartitionKeyValue,
            CountedUntil = countedUntil
        };

        // "articleSlug" is the Comments container's partition key path, so
        // it must be set even though this isn't a comment.
        private sealed class WatermarkDocument
        {
            [JsonProperty("id")]
            public string Id { get; set; } = "";

            [JsonProperty("articleSlug")]
            public string ArticleSlug { get; set; } = "";

            [JsonProperty("countedUntil")]
            public DateTime CountedUntil { get; set; }
        }
    }
}
