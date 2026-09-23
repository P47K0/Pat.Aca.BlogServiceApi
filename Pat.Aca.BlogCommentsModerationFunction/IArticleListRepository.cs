namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Reads the newest published articles directly from the Articles
    /// container. Separate interface from ArticleListSyncProcessor for the
    /// same testability reason as IArticleCountRepository/
    /// IMostViewedArticleRepository.
    /// </summary>
    public interface IArticleListRepository
    {
        Task<List<ArticleListItem>> GetLatestArticlesAsync(CancellationToken cancellationToken = default);
    }
}
