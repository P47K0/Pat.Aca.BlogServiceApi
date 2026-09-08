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
        string? LinkedinVideoEmbedUrl = null);
}
