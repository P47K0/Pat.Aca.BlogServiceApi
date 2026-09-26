using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IMostViewedArticleRepository implementation -- the same query
    /// as Pat.Aca.BlogServiceApi's CosmosArticleRepository.GetMostViewedArticleAsync,
    /// deliberately duplicated (see CosmosArticleCountRepository's own doc
    /// comment for why). Keeps the future-publishedAt and unlisted
    /// exclusions, unlike CosmosArticleCountRepository's count query --
    /// this points a homepage link at something clickable, so it must
    /// never surface a scheduled or unlisted article.
    ///
    /// Reads the same shared Articles Container as CosmosArticleCountRepository.
    /// Not unit tested, consistent with this project's practice for its
    /// other real Cosmos-calling classes.
    /// </summary>
    public sealed class CosmosMostViewedArticleRepository : IMostViewedArticleRepository
    {
        private readonly Container _articlesContainer;

        public CosmosMostViewedArticleRepository(Container articlesContainer)
        {
            _articlesContainer = articlesContainer;
        }

        public async Task<MostViewedArticleResult?> GetMostViewedArticleAsync(CancellationToken cancellationToken = default)
        {
            var query = new QueryDefinition(
                "SELECT TOP 1 c.slug, c.title, c.summary, c.viewCount, c.coverImageUrl FROM c " +
                "WHERE c.publishedAt <= @now AND (NOT IS_DEFINED(c.unlisted) OR c.unlisted = false) " +
                "ORDER BY c.viewCount DESC")
                .WithParameter("@now", DateTime.UtcNow);

            using FeedIterator<MostViewedArticleDocument> iterator =
                _articlesContainer.GetItemQueryIterator<MostViewedArticleDocument>(query);

            while (iterator.HasMoreResults)
            {
                FeedResponse<MostViewedArticleDocument> response = await iterator.ReadNextAsync(cancellationToken);
                var top = response.Resource.FirstOrDefault();
                if (top is not null)
                {
                    return new MostViewedArticleResult(top.Slug, top.Title, top.Summary, top.ViewCount, top.CoverImageUrl);
                }
            }

            return null;
        }

        private sealed class MostViewedArticleDocument
        {
            [JsonProperty("slug")]
            public string Slug { get; set; } = string.Empty;

            [JsonProperty("title")]
            public string Title { get; set; } = string.Empty;

            [JsonProperty("summary")]
            public string Summary { get; set; } = string.Empty;

            [JsonProperty("viewCount")]
            public int ViewCount { get; set; }

            [JsonProperty("coverImageUrl")]
            public string? CoverImageUrl { get; set; }
        }
    }
}
