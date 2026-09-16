using Microsoft.Azure.Cosmos;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleCountRepository implementation -- the exact same query
    /// as Pat.Aca.BlogServiceApi's CosmosArticleRepository.GetArticleCountAsync,
    /// deliberately duplicated by hand here rather than referenced from that
    /// project (same reasoning as ModerationCommentStatus duplicating
    /// CommentStatus's values: an independently-deployed Function
    /// referencing the ASP.NET Core API project just to reuse one query
    /// string would drag unrelated hosting deps into this Function's own
    /// cold-start path -- the two processes only need to agree on the
    /// counting rule, not share code). See
    /// IArticleRepository.GetArticleCountAsync's own doc comment in that
    /// project for why there's deliberately no publishedAt filter: this
    /// counts every real blog post, published or scheduled, excluding only
    /// Unlisted content -- which is what makes the count depend solely on
    /// Cosmos writes (what this Function's Change Feed trigger reacts to),
    /// never on wall-clock time alone.
    ///
    /// Reads the same shared Articles Container Program.cs already builds
    /// for CosmosArticleContextProvider -- no second client/connection.
    /// Not unit tested, consistent with this project's existing practice
    /// for its other real Cosmos-calling classes (CosmosArticleContextProvider,
    /// CosmosModerationQuotaStore, CommentStatusWriter).
    /// </summary>
    public sealed class CosmosArticleCountRepository : IArticleCountRepository
    {
        private readonly Container _articlesContainer;

        public CosmosArticleCountRepository(Container articlesContainer)
        {
            _articlesContainer = articlesContainer;
        }

        public async Task<int> GetArticleCountAsync(CancellationToken cancellationToken = default)
        {
            var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE (NOT IS_DEFINED(c.unlisted) OR c.unlisted = false)");

            using FeedIterator<int> iterator = _articlesContainer.GetItemQueryIterator<int>(query);
            var count = 0;
            while (iterator.HasMoreResults)
            {
                FeedResponse<int> response = await iterator.ReadNextAsync(cancellationToken);
                count += response.Resource.FirstOrDefault();
            }

            return count;
        }
    }
}
