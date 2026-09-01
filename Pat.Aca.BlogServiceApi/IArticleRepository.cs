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

        /// <summary>
        /// Creates a new article. Returns null if an article with this slug
        /// already exists — including a not-yet-published (future publishedAt)
        /// one, since an author must be able to reserve/see their own drafts,
        /// unlike the read methods above which hide those from the public.
        /// Callers (Program.cs) treat null as 409 Conflict; slug-collision
        /// retry (postfixing) happens client-side, not here — per the BRD, the
        /// caller generates slugs before calling POST.
        /// </summary>
        Task<Article?> CreateArticleAsync(ArticleWriteRequest request);

        /// <summary>
        /// Full-replace update of an existing article, keyed by slug —
        /// update-only, never upsert: returns null if no article (published or
        /// draft) has this slug, which callers treat as 404. ViewCount is
        /// always preserved server-side, never reset by a write.
        /// </summary>
        Task<Article?> UpdateArticleAsync(string slug, ArticleWriteRequest request);
    }
}
