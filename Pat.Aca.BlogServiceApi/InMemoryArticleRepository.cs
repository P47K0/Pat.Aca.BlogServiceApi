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

        public Task<Article?> GetArticleBySlugAsync(string slug) =>
            Task.FromResult(SeedArticles.FirstOrDefault(a => a.Slug == slug && a.PublishedAt <= DateTime.UtcNow));
    }
}
