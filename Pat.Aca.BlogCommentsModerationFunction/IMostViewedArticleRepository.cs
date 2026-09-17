namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Reads the current most-viewed published article directly from the
    /// Articles container. Separate interface from MostViewedSyncProcessor
    /// for the same testability reason as IArticleCountRepository.
    /// </summary>
    public interface IMostViewedArticleRepository
    {
        Task<MostViewedArticleResult?> GetMostViewedArticleAsync(CancellationToken cancellationToken = default);
    }
}
