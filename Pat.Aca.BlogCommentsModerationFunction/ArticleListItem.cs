namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// One article as pushed to api-proxy's durable list snapshot -- mirrors
    /// the sibling API project's Article record, minus <c>Content</c>: the
    /// snapshot api-proxy actually serves always blanks it anyway (the list
    /// view never needs it, see api-proxy's own FallbackSnapshot doc
    /// comment), so there's no reason to pay the extra Cosmos RU/payload
    /// size fetching Markdown bodies this sync will never use. Since
    /// api-proxy serves /articles from this push alone, every other field
    /// ui-worker reads off a list item has to be here too: the series and
    /// related lookups on the article page, and <c>UpdatedAt</c> for the
    /// sitemap's lastmod.
    /// </summary>
    public sealed record ArticleListItem(
        int Id,
        string Slug,
        string Title,
        string Summary,
        string PublishedAt,
        IReadOnlyList<string> Tags,
        int ViewCount,
        string? LinkedinVideoEmbedUrl,
        string? CoverImageUrl,
        string? SeoDescription,
        IReadOnlyList<string>? SeoKeywords,
        string? SeriesName,
        int? SeriesOrder,
        IReadOnlyList<string>? RelatedSlugs,
        DateTime? UpdatedAt);
}
