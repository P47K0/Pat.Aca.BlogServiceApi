namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Reads every listed, published article directly from the Articles
    /// container. Separate interface from ArticleListSyncProcessor for the
    /// same testability reason as IArticleCountRepository/
    /// IMostViewedArticleRepository.
    /// </summary>
    public interface IArticleListRepository
    {
        Task<List<ArticleListItem>> GetListedArticlesAsync(CancellationToken cancellationToken = default);
    }
}
