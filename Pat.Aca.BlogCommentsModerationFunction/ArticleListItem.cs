namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// One article as pushed to api-proxy's durable list snapshot -- mirrors
    /// the sibling API project's Article record, minus <c>Content</c>: the
    /// snapshot api-proxy actually serves always blanks it anyway (the list
    /// view never needs it, see api-proxy's own FallbackSnapshot doc
    /// comment), so there's no reason to pay the extra Cosmos RU/payload
    /// size fetching Markdown bodies this sync will never use.
    /// </summary>
    public sealed record ArticleListItem(
        int Id,
        string Slug,
        string Title,
        string Summary,
        string PublishedAt,
        IReadOnlyList<string> Tags,
        int ViewCount,
        string? LinkedinVideoEmbedUrl);
}
