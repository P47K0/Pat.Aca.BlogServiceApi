namespace Pat.Aca.BlogServiceApi
{
    public class InMemoryArticleRepository : IArticleRepository
    {
        private static readonly List<Article> SeedArticles = new()
        {
            new Article(1, "first-article", "First Article", "Summary of first article", "Content of first article", DateTime.UtcNow.AddDays(-2), new List<string> { "tag1" }),
            new Article(2, "second-article", "Second Article", "Summary of second article", "Content of second article", DateTime.UtcNow.AddDays(-1), new List<string> { "tag2" })
        };

        public Task<List<Article>> GetArticlesAsync() =>
            // Mirrors CosmosArticleRepository: newest-first, future publishedAt excluded.
            Task.FromResult(SeedArticles
                .Where(a => a.PublishedAt <= DateTime.UtcNow)
                .OrderByDescending(a => a.PublishedAt)
                .ToList());

        public async Task<ArticlesPage> GetArticlesPageAsync(int limit, string? afterSlug)
        {
            var published = await GetArticlesAsync();

            // Unknown/stale cursor falls back to the first page (index 0) —
            // see the doc comment on IArticleRepository.GetArticlesPageAsync.
            var startIndex = 0;
            if (!string.IsNullOrEmpty(afterSlug))
            {
                var cursorIndex = published.FindIndex(a => a.Slug == afterSlug);
                if (cursorIndex >= 0)
                {
                    startIndex = cursorIndex + 1;
                }
            }

            var page = published.Skip(startIndex).Take(limit).ToList();
            var hasMore = startIndex + page.Count < published.Count;
            return new ArticlesPage(page, hasMore, hasMore ? page[^1].Slug : null);
        }

        public Task<Article?> GetArticleBySlugAsync(string slug) =>
            Task.FromResult(SeedArticles.FirstOrDefault(a => a.Slug == slug && a.PublishedAt <= DateTime.UtcNow));

        public Task<Article?> IncrementViewCountAsync(string slug)
        {
            var index = SeedArticles.FindIndex(a => a.Slug == slug && a.PublishedAt <= DateTime.UtcNow);
            if (index < 0)
            {
                return Task.FromResult<Article?>(null);
            }

            var updated = SeedArticles[index] with { ViewCount = SeedArticles[index].ViewCount + 1 };
            SeedArticles[index] = updated;
            return Task.FromResult<Article?>(updated);
        }

        public Task<Article?> CreateArticleAsync(ArticleWriteRequest request)
        {
            // No publishedAt filter — a draft/future-dated slug still reserves
            // the name, mirroring CosmosArticleRepository.
            if (SeedArticles.Any(a => a.Slug == request.Slug))
            {
                return Task.FromResult<Article?>(null);
            }

            // Id dropped from the write path per the BRD (legacy, never used
            // for lookups) — new articles just get 0.
            var article = new Article(0, request.Slug, request.Title, request.Summary ?? "", request.Content, request.PublishedAt, request.Tags ?? new List<string>());
            SeedArticles.Add(article);
            return Task.FromResult<Article?>(article);
        }

        public Task<Article?> UpdateArticleAsync(string slug, ArticleWriteRequest request)
        {
            var index = SeedArticles.FindIndex(a => a.Slug == slug);
            if (index < 0)
            {
                return Task.FromResult<Article?>(null);
            }

            // ViewCount (and Id) deliberately untouched — full-replace from the
            // client's perspective, but these two stay server-owned.
            var updated = SeedArticles[index] with
            {
                Title = request.Title,
                Summary = request.Summary ?? "",
                Content = request.Content,
                PublishedAt = request.PublishedAt,
                Tags = request.Tags ?? new List<string>()
            };
            SeedArticles[index] = updated;
            return Task.FromResult<Article?>(updated);
        }
    }
}
