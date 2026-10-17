using Microsoft.Azure.Cosmos;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Adds a batch of views to an article's viewCount with one atomic Patch
    /// Increment, the same operation the API's IncrementViewCountAsync used
    /// per read. Looks the Cosmos id up by slug first (the Articles
    /// partition key is /slug, the id is a different value). Needs the
    /// container-scoped "Articles view-count writer" role in
    /// infra/cosmos-db.bicep (items/replace only). Not unit tested, like this
    /// project's other Cosmos classes.
    /// </summary>
    public sealed class CosmosArticleViewCountWriter(Container articlesContainer) : IArticleViewCountWriter
    {
        public async Task<bool> AddViewsAsync(string slug, long views, CancellationToken cancellationToken = default)
        {
            var idQuery = new QueryDefinition("SELECT VALUE c.id FROM c WHERE c.slug = @slug")
                .WithParameter("@slug", slug);

            string? cosmosId = null;
            using (FeedIterator<string> iterator = articlesContainer.GetItemQueryIterator<string>(
                idQuery, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(slug) }))
            {
                while (iterator.HasMoreResults && cosmosId is null)
                {
                    var page = await iterator.ReadNextAsync(cancellationToken);
                    cosmosId = page.Resource.FirstOrDefault();
                }
            }

            if (cosmosId is null)
            {
                return false;
            }

            await articlesContainer.PatchItemAsync<object>(
                cosmosId,
                new PartitionKey(slug),
                new[] { PatchOperation.Increment("/viewCount", views) },
                new PatchItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken);
            return true;
        }
    }
}
