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
        List<string>? RelatedSlugs = null);
}
