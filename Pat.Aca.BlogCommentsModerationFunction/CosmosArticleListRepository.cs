using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleListRepository implementation -- the same newest-first,
    /// future-publishedAt-excluded, Unlisted-excluded query as the sibling
    /// API project's CosmosArticleRepository.GetArticlesAsync, deliberately
    /// duplicated (see CosmosArticleCountRepository's own doc comment for
    /// why), capped to the top 10 -- matching api-proxy's own FALLBACK_SIZE
    /// constant for the durable list snapshot this feeds. Unlike
    /// GetArticleCountAsync's deliberate difference, this keeps the
    /// future-publishedAt filter: the snapshot it feeds is what readers
    /// actually see on the homepage, so a scheduled article must stay
    /// invisible until its own publishedAt actually passes -- the same
    /// reason this sync can't rely on Change Feed deliveries alone for
    /// *this* particular field (see ArticleListSyncFunction's own doc
    /// comment).
    ///
    /// Reads the same shared Articles Container as CosmosArticleCountRepository/
    /// CosmosMostViewedArticleRepository. Not unit tested, consistent with
    /// this project's practice for its other real Cosmos-calling classes.
    /// </summary>
    public sealed class CosmosArticleListRepository : IArticleListRepository
    {
        // Matches api-proxy's FALLBACK_SIZE constant in index.ts -- keep the
        // two in sync if that value ever changes.
        private const int LatestCount = 10;

        private readonly Container _articlesContainer;

        public CosmosArticleListRepository(Container articlesContainer)
        {
            _articlesContainer = articlesContainer;
        }

        public async Task<List<ArticleListItem>> GetLatestArticlesAsync(CancellationToken cancellationToken = default)
        {
            var query = new QueryDefinition(
                "SELECT TOP @limit c.id, c.slug, c.title, c.summary, c.publishedAt, c.tags, " +
                "c.viewCount, c.linkedinVideoEmbedUrl FROM c " +
                "WHERE c.publishedAt <= @now AND (NOT IS_DEFINED(c.unlisted) OR c.unlisted = false) " +
                "ORDER BY c.publishedAt DESC")
                .WithParameter("@limit", LatestCount)
                .WithParameter("@now", DateTime.UtcNow);

            using FeedIterator<ArticleListDocument> iterator =
                _articlesContainer.GetItemQueryIterator<ArticleListDocument>(query);

            var articles = new List<ArticleListItem>();
            while (iterator.HasMoreResults)
            {
                FeedResponse<ArticleListDocument> response = await iterator.ReadNextAsync(cancellationToken);
                articles.AddRange(response.Resource.Select(document => new ArticleListItem(
                    document.Id,
                    document.Slug,
                    document.Title,
                    document.Summary,
                    document.PublishedAt,
                    document.Tags,
                    document.ViewCount,
                    document.LinkedinVideoEmbedUrl)));
            }

            return articles;
        }

        private sealed class ArticleListDocument
        {
            [JsonProperty("id")]
            public int Id { get; set; }

            [JsonProperty("slug")]
            public string Slug { get; set; } = string.Empty;

            [JsonProperty("title")]
            public string Title { get; set; } = string.Empty;

            [JsonProperty("summary")]
            public string Summary { get; set; } = string.Empty;

            [JsonProperty("publishedAt")]
            public string PublishedAt { get; set; } = string.Empty;

            [JsonProperty("tags")]
            public List<string> Tags { get; set; } = new();

            [JsonProperty("viewCount")]
            public int ViewCount { get; set; }

            [JsonProperty("linkedinVideoEmbedUrl")]
            public string? LinkedinVideoEmbedUrl { get; set; }
        }
    }
}
