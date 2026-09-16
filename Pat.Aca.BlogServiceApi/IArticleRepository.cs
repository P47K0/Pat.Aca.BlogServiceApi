namespace Pat.Aca.BlogServiceApi
{
    public interface IArticleRepository
    {
        /// <summary>
        /// Newest-first, future-publishedAt-excluded, and now also excludes
        /// any article with <see cref="Article.Unlisted"/> set — an
        /// unlisted article is still directly fetchable via <see
        /// cref="GetArticleBySlugAsync"/>, just never shows up in this list
        /// (or anything derived from it: the homepage, sitemap.xml,
        /// feed.xml, the tag cloud).
        /// </summary>
        Task<List<Article>> GetArticlesAsync();

        /// <summary>
        /// Cursor-paginated slice of the same newest-first, future-excluded
        /// ordering as <see cref="GetArticlesAsync"/> — powers the blog
        /// homepage's "load more" infinite scroll (article 11 onward; the
        /// first 10 are still rendered from a plain <see
        /// cref="GetArticlesAsync"/> call, since the tag cloud already needs
        /// the full list on every home page load anyway). <paramref
        /// name="afterSlug"/> is the previous page's last article's slug, or
        /// null/empty for the first page. An <paramref name="afterSlug"/>
        /// that doesn't match any published article (deleted, or simply
        /// wrong) is treated the same as no cursor — gracefully falls back
        /// to the first page rather than erroring, since a stale
        /// client-held cursor is an expected, not exceptional, case.
        /// </summary>
        Task<ArticlesPage> GetArticlesPageAsync(int limit, string? afterSlug);

        Task<Article?> GetArticleBySlugAsync(string slug);

        /// <summary>
        /// Count of every real blog post, published or scheduled — excludes
        /// only <see cref="Article.Unlisted"/> content (e.g. /about), NOT
        /// future-<c>publishedAt</c> articles (a deliberate difference from
        /// <see cref="GetArticlesAsync"/>). Without paying for every
        /// article's full payload just to read a length. Backs the site
        /// homepage's blog-post counter, kept current in Cloudflare KV via a
        /// Cosmos DB Change Feed-triggered Function so the read path never
        /// has to hit this API directly (see the "blog-post counter" backlog
        /// item) — the future-publishedAt exclusion was dropped specifically
        /// so this count only changes on an actual Cosmos write, never on
        /// wall-clock time alone.
        /// </summary>
        Task<int> GetArticleCountAsync();

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
