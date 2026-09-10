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

        /// <summary>
        /// Gives back a slot previously claimed by TryConsumeAsync, for when
        /// the scoring attempt it was reserved for turned out to fail (an
        /// infra failure -- Cloudflare unreachable, a bad token, etc. --
        /// never a real, successful moderation). Without this, a persistent
        /// failure burns through the whole day's quota on attempts that
        /// never actually moderated anything -- exactly what happened in
        /// production on 2026-09-10 (an invalid Cloudflare API token caused
        /// every attempt to fail with a 401, silently exhausting the day's
        /// quota with zero comments ever actually scored). Best-effort: a
        /// lost release (e.g. a UTC day boundary crossed between consume and
        /// release) just means one quota slot is under-counted for the rest
        /// of that day, not a correctness problem worth failing loudly over.
        /// </summary>
        Task ReleaseAsync();
    }
}
