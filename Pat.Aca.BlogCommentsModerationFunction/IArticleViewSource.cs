namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>Article views counted at the edge, per slug.</summary>
    public interface IArticleViewSource
    {
        /// <summary>Views per slug with a timestamp in [from, to), both UTC.</summary>
        Task<IReadOnlyDictionary<string, long>> GetViewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }
}
