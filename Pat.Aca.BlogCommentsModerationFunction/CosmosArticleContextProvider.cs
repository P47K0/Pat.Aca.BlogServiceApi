using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleContextProvider implementation -- reads the Articles
    /// container directly (same database as Comments, a different
    /// container). Program.cs builds this Container from the same shared
    /// CosmosClient the rest of the Function already uses, rather than a
    /// second client -- same reasoning as CosmosModerationQuotaStore/
    /// CommentStatusWriter sharing the Comments Container. Only ever needs
    /// c.summary, never the full article, so this is a narrow SELECT VALUE
    /// query rather than a full-document read.
    ///
    /// Deliberately doesn't apply the API project's future-dated-exclusion
    /// business rule (see CosmosArticleRepository.GetArticleBySlugAsync) --
    /// this is best-effort context for a comment that already exists, not a
    /// public-facing read, so there's nothing to protect by excluding it.
    ///
    /// Best-effort by design: any failure is caught and logged, returning
    /// null rather than throwing -- see IArticleContextProvider's own doc
    /// comment for why this must never block moderation itself. Not
    /// directly unit tested, consistent with this project's existing
    /// practice for its other real Cosmos-calling classes.
    /// </summary>
    public sealed class CosmosArticleContextProvider : IArticleContextProvider
    {
        private readonly Container _articlesContainer;
        private readonly ILogger<CosmosArticleContextProvider> _logger;

        public CosmosArticleContextProvider(Container articlesContainer, ILogger<CosmosArticleContextProvider> logger)
        {
            _articlesContainer = articlesContainer;
            _logger = logger;
        }

        public async Task<string?> GetArticleSummaryAsync(string articleSlug)
        {
            try
            {
                var query = new QueryDefinition("SELECT VALUE c.summary FROM c WHERE c.slug = @slug")
                    .WithParameter("@slug", articleSlug);

                using FeedIterator<string> iterator = _articlesContainer.GetItemQueryIterator<string>(query);
                if (!iterator.HasMoreResults)
                {
                    return null;
                }

                FeedResponse<string> response = await iterator.ReadNextAsync();
                return response.Resource.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not look up the article summary for {ArticleSlug} -- scoring without article context.",
                    articleSlug);
                return null;
            }
        }
    }
}
