using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IMostViewedArticleRepository implementation. Picks the article
    /// with the most views from outside referrers (search engines, LinkedIn,
    /// forums; not direct visits or the user's own sites) over the last
    /// TrendingDays days, random among ties. When there are no such views
    /// yet (or Analytics Engine is unreachable) it falls back to the
    /// all-time top viewCount -- the same query as Pat.Aca.BlogServiceApi's
    /// CosmosArticleRepository.GetMostViewedArticleAsync, deliberately
    /// duplicated (see CosmosArticleCountRepository's own doc comment for
    /// why), so the tile never goes empty. Keeps the future-publishedAt and unlisted
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
        public const int TrendingDays = 7;

        private readonly Container _articlesContainer;
        private readonly IExternalReferrerViewSource _externalViews;
        private readonly Random _random;

        public CosmosMostViewedArticleRepository(Container articlesContainer, IExternalReferrerViewSource externalViews, Random? random = null)
        {
            _articlesContainer = articlesContainer;
            _externalViews = externalViews;
            _random = random ?? Random.Shared;
        }

        public async Task<MostViewedArticleResult?> GetMostViewedArticleAsync(CancellationToken cancellationToken = default)
        {
            return await GetTrendingAsync(cancellationToken) ?? await GetAllTimeTopAsync(cancellationToken);
        }

        private async Task<MostViewedArticleResult?> GetTrendingAsync(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, long> views;
            try
            {
                var now = DateTime.UtcNow;
                views = await _externalViews.GetExternalViewsAsync(now.AddDays(-TrendingDays), now, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            // Only published, listed articles may be picked (an unlisted or
            // scheduled one can have views too), so take the best slug
            // among those the Articles container still returns.
            var remaining = new Dictionary<string, long>(views);
            while (remaining.Count > 0)
            {
                var slug = TrendingArticlePicker.PickSlug(remaining, _random);
                if (slug is null) return null;
                var article = await GetEligibleAsync(slug, cancellationToken);
                if (article is not null) return article;
                remaining.Remove(slug);
            }
            return null;
        }

        private async Task<MostViewedArticleResult?> GetEligibleAsync(string slug, CancellationToken cancellationToken)
        {
            var query = new QueryDefinition(
                "SELECT TOP 1 c.slug, c.title, c.summary, c.viewCount, c.coverImageUrl FROM c " +
                "WHERE c.slug = @slug AND c.publishedAt <= @now AND (NOT IS_DEFINED(c.unlisted) OR c.unlisted = false)")
                .WithParameter("@slug", slug)
                .WithParameter("@now", DateTime.UtcNow);
            using FeedIterator<MostViewedArticleDocument> iterator =
                _articlesContainer.GetItemQueryIterator<MostViewedArticleDocument>(query);
            while (iterator.HasMoreResults)
            {
                var top = (await iterator.ReadNextAsync(cancellationToken)).Resource.FirstOrDefault();
                if (top is not null)
                {
                    return new MostViewedArticleResult(top.Slug, top.Title, top.Summary, top.ViewCount, top.CoverImageUrl);
                }
            }
            return null;
        }

        private async Task<MostViewedArticleResult?> GetAllTimeTopAsync(CancellationToken cancellationToken)
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
