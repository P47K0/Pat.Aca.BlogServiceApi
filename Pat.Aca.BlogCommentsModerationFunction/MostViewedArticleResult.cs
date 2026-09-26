namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The single most-viewed, published, listed article -- mirrors the
    /// sibling API project's MostViewedArticle shape, deliberately
    /// duplicated rather than shared (same reasoning as
    /// CosmosArticleCountRepository's own doc comment: no project
    /// reference just to reuse one small type).
    /// </summary>
    public sealed record MostViewedArticleResult(string Slug, string Title, string Summary, int ViewCount, string? CoverImageUrl = null);
}
