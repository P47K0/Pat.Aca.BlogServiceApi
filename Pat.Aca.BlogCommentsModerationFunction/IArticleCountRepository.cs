namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Reads the current blog-post count directly from the Articles
    /// container. A separate interface from ArticleCountSyncProcessor
    /// (rather than inlining the query there) so the processor itself stays
    /// unit-testable with a fake, the same "one interface per external
    /// dependency" shape as IModerationScorer/IModerationQuotaStore/
    /// IModerationNotifier.
    /// </summary>
    public interface IArticleCountRepository
    {
        Task<int> GetArticleCountAsync(CancellationToken cancellationToken = default);
    }
}
