namespace Pat.Aca.BlogServiceApi
{
    public record Article(
        int Id,
        string Slug,
        string Title,
        string Summary,
        string Content,
        DateTime PublishedAt,
        List<string> Tags,
        int ViewCount = 0,
        // LinkedIn's own "Embed video only" iframe src for this article's demo
        // video (https://www.linkedin.com/embed/feed/update/urn:li:ugcPost:{id}),
        // or null for an article with no video. A deliberate dependency on the
        // source LinkedIn post staying up/Public — not self-hosted, unlike this
        // project's images (re-uploaded to R2) — accepted for the much lower
        // effort of embedding vs. downloading and re-hosting each video.
        string? LinkedinVideoEmbedUrl = null,
        // Strict ordinal series (e.g. "Debugging Skills" 1/2/3) — both null for
        // an article that isn't part of a numbered series. Both are set
        // together or not at all; no validation enforces that pairing today.
        string? SeriesName = null,
        int? SeriesOrder = null,
        // Looser, non-ordinal cross-links to other articles by slug (e.g. all
        // posts about the same side project) — for clusters that don't fit a
        // clean numbered sequence. Deliberately just slugs, no separate
        // cluster/group name field. Not validated for symmetry or that a
        // referenced slug actually exists.
        List<string>? RelatedSlugs = null,
        // Excludes this article from GetArticlesAsync/GetArticlesPageAsync/
        // GetArticleCountAsync (and therefore the homepage, sitemap.xml,
        // feed.xml, and the tag cloud, all of which derive from those) while
        // GetArticleBySlugAsync still returns it directly — for content that
        // needs a real, fetchable slug but isn't a blog post (e.g. the /about
        // CV-like document). Defaults false so every existing article stays
        // listed exactly as before this field was added.
        bool Unlisted = false,
        // Absolute https URL of this article's cover image (a pre-resized
        // 1200x630 JPEG on images.koorevaar.com, generated once at write/
        // backfill time rather than transformed per request), or null for
        // an article without one. Used for og:image/twitter:image and the
        // homepage's most-viewed thumbnail; both simply omit the image
        // when this is null.
        string? CoverImageUrl = null,
        // Search-snippet description, written distinctly from Summary
        // (Summary stays the human-facing card blurb). Rendered as the
        // meta/og/twitter description when present, falling back to
        // Summary when null. Max 160 characters (see ArticleWriteValidation).
        string? SeoDescription = null,
        // Curated search keywords, distinct from Tags (Tags stay the site's
        // navigation taxonomy). Rendered as <meta name="keywords"> and the
        // JSON-LD keywords when present, falling back to Tags for JSON-LD.
        List<string>? SeoKeywords = null,
        // Markdown rendered below the article body as a visually separate
        // block (e.g. the "Co-authored with Claude." byline), rather than
        // appended to Content. Kept out of Content so it isn't embedded as
        // its own KnowledgeBase chunk on every article. Null for none.
        string? Footer = null);
}
