using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleListRepository implementation -- the same newest-first,
    /// future-publishedAt-excluded, Unlisted-excluded query as the sibling
    /// API project's CosmosArticleRepository.GetArticlesAsync, deliberately
    /// duplicated (see CosmosArticleCountRepository's own doc comment for
    /// why). Returns every listed article, not only the newest 10: api-proxy
    /// stores the full list, so a pushed snapshot is never smaller than the
    /// list a colo already has cached (the 36-of-152 tag cloud, 2026-10-15).
    /// Unlike
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
        private readonly Container _articlesContainer;

        public CosmosArticleListRepository(Container articlesContainer)
        {
            _articlesContainer = articlesContainer;
        }

        public async Task<List<ArticleListItem>> GetListedArticlesAsync(CancellationToken cancellationToken = default)
        {
            var query = new QueryDefinition(
                "SELECT c.slug, c.title, c.summary, c.publishedAt, c.tags, " +
                "c.viewCount, c.linkedinVideoEmbedUrl, c.coverImageUrl, c.seoDescription, c.seoKeywords, " +
                "c.seriesName, c.seriesOrder, c.relatedSlugs, c.updatedAt FROM c " +
                "WHERE c.publishedAt <= @now AND (NOT IS_DEFINED(c.unlisted) OR c.unlisted = false) " +
                "ORDER BY c.publishedAt DESC")
                .WithParameter("@now", DateTime.UtcNow);

            using FeedIterator<ArticleListDocument> iterator =
                _articlesContainer.GetItemQueryIterator<ArticleListDocument>(query);

            var articles = new List<ArticleListItem>();
            while (iterator.HasMoreResults)
            {
                FeedResponse<ArticleListDocument> response = await iterator.ReadNextAsync(cancellationToken);
                // Id is always 0, as the API returns it: the Cosmos `id` is a
                // GUID string on API-created articles, not the legacy int.
                articles.AddRange(response.Resource.Select(document => new ArticleListItem(
                    0,
                    document.Slug,
                    document.Title,
                    document.Summary,
                    document.PublishedAt,
                    document.Tags,
                    document.ViewCount,
                    document.LinkedinVideoEmbedUrl,
                    document.CoverImageUrl,
                    document.SeoDescription,
                    document.SeoKeywords,
                    document.SeriesName,
                    document.SeriesOrder,
                    document.RelatedSlugs,
                    document.UpdatedAt)));
            }

            return articles;
        }

        private sealed class ArticleListDocument
        {
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

            [JsonProperty("coverImageUrl")]
            public string? CoverImageUrl { get; set; }

            [JsonProperty("seoDescription")]
            public string? SeoDescription { get; set; }

            [JsonProperty("seoKeywords")]
            public List<string>? SeoKeywords { get; set; }

            [JsonProperty("seriesName")]
            public string? SeriesName { get; set; }

            [JsonProperty("seriesOrder")]
            public int? SeriesOrder { get; set; }

            [JsonProperty("relatedSlugs")]
            public List<string>? RelatedSlugs { get; set; }

            [JsonProperty("updatedAt")]
            public DateTime? UpdatedAt { get; set; }
        }
    }
}
