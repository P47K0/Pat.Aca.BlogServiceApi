namespace Pat.Aca.BlogServiceApi
{
    public interface IArticleRepository
    {
        Task<List<Article>> GetArticlesAsync();
        Task<Article?> GetArticleBySlugAsync(string slug);

        /// <summary>
        /// Atomically-intended increment of an article's view count. Returns the
        /// updated article, or null if <paramref name="slug"/> doesn't match a
        /// published article (mirrors <see cref="GetArticleBySlugAsync"/>'s
        /// future-publishedAt exclusion — an unpublished/scheduled article's
        /// views aren't counted).
        /// </summary>
        Task<Article?> IncrementViewCountAsync(string slug);
    }
}
