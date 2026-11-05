namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Article views that came from outside the user's own sites (a search
    /// engine, LinkedIn, a forum), per slug. Direct visits and referrers
    /// containing "koorevaar" do not count, same rule as the ui-worker's
    /// /referrers.json?source=external.
    /// </summary>
    public interface IExternalReferrerViewSource
    {
        /// <summary>External-referrer views per slug with a timestamp in [from, to), both UTC.</summary>
        Task<IReadOnlyDictionary<string, long>> GetExternalViewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    }
}
