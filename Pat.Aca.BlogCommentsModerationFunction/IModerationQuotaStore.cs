namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Tracks how many comments have been scored today against
    /// ModerationSettings.DailyModerationQuota. The real implementation (a
    /// later commit) persists the counter/reset date somewhere durable
    /// (likely a small Cosmos document, reusing infra already in place
    /// rather than adding a new resource -- per the design's own
    /// not-yet-decided note); kept behind this interface so the processor's
    /// quota-respecting behavior is testable without it.
    /// </summary>
    public interface IModerationQuotaStore
    {
        /// <summary>
        /// Attempts to consume one of today's quota slots. Returns false if
        /// today's quota is already exhausted -- the caller leaves the
        /// comment at ModerationCommentStatus.Queued for a later run to
        /// pick up, rather than scoring it anyway.
        /// </summary>
        Task<bool> TryConsumeAsync();
    }
}
