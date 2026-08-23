// Deliberately in the global namespace: Article/IArticleRepository are declared as
// top-level types in Program.cs (no namespace), so this matches them without
// qualification and can be swapped in for CosmosArticleRepository in tests.
public sealed class FakeArticleRepository : IArticleRepository
{
    private static readonly List<Article> SeedArticles = new()
    {
        new Article(1, "first-article", "First Article", "Summary of first article", "Content of first article", DateTime.UtcNow, new List<string> { "tag1" }),
        new Article(2, "second-article", "Second Article", "Summary of second article", "Content of second article", DateTime.UtcNow, new List<string> { "tag2" })
    };

    public Task<List<Article>> GetArticlesAsync() => Task.FromResult(SeedArticles);

    public Task<Article?> GetArticleBySlugAsync(string slug) =>
        Task.FromResult(SeedArticles.FirstOrDefault(a => a.Slug == slug));
}
