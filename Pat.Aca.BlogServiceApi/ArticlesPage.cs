namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// One page of the newest-first article ordering, returned by
    /// <see cref="IArticleRepository.GetArticlesPageAsync"/>. NextCursor is
    /// the slug to pass back as <c>after</c> to fetch the next page — null
    /// once HasMore is false.
    /// </summary>
    public sealed record ArticlesPage(List<Article> Items, bool HasMore, string? NextCursor);
}
