namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Response shape for GET /articles/most-viewed -- just enough to render
    /// a homepage link (slug/title/summary) plus the number itself, not a
    /// full Article. Same "don't pay for the full payload" reasoning as
    /// GetArticleCountAsync.
    /// </summary>
    public sealed record MostViewedArticle(string Slug, string Title, string Summary, int ViewCount);
}
