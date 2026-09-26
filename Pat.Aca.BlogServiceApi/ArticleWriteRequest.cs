namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Request body for POST/PUT /articles. Deliberately excludes Id (legacy
    /// field dropped from the write path per the BRD — a hand-typed int never
    /// used for lookups anywhere; slug is the real key) and ViewCount
    /// (server-owned, only ever changed by GET /articles/{slug}'s increment,
    /// never client-settable). Only Slug/Title/Content are enforced-required
    /// by ArticleWriteValidation — Summary/PublishedAt/Tags are pass-through.
    /// </summary>
    public record ArticleWriteRequest(
        string Slug,
        string Title,
        string Content,
        string? Summary = null,
        DateTime PublishedAt = default,
        List<string>? Tags = null,
        // Pass-through, same as Summary/PublishedAt/Tags — no required
        // validation. See Article.LinkedinVideoEmbedUrl for the field's
        // purpose/trade-off.
        string? LinkedinVideoEmbedUrl = null,
        // Pass-through, no required validation. See Article.SeriesName/
        // SeriesOrder/RelatedSlugs for what these mean.
        string? SeriesName = null,
        int? SeriesOrder = null,
        List<string>? RelatedSlugs = null,
        // Pass-through, no required validation. See Article.Unlisted for
        // what this means.
        bool Unlisted = false,
        // Optional, but when present must be an absolute https URL (see
        // ArticleWriteValidation). See Article.CoverImageUrl.
        string? CoverImageUrl = null,
        // Optional, validated when present (see ArticleWriteValidation).
        // See Article.SeoDescription/SeoKeywords.
        string? SeoDescription = null,
        List<string>? SeoKeywords = null,
        // Optional, but when present must be non-blank (see
        // ArticleWriteValidation). See Article.Footer.
        string? Footer = null);
}
