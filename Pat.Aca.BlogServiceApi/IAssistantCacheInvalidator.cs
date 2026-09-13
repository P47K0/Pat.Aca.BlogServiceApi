namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Phase 5 of the AI assistant project: tells the assistant-worker
    /// Cloudflare Worker that KnowledgeBase content it may have cached an
    /// answer against has changed, so it can invalidate that cache. Called
    /// right after a successful article create/update — the one write path
    /// ACA itself knows about (KnowledgeBase chunk/profile-fact writes
    /// happen client-side, outside ACA, per this project's embed-at-write-
    /// time convention, so they aren't covered by this call).
    ///
    /// Deliberately best-effort: a failed/unreachable call here never fails
    /// the article write itself, it just means the assistant Worker may
    /// serve a stale cached answer until its own semantic-cache lookup
    /// naturally evicts it or someone retries. See
    /// AssistantWorkerCacheInvalidator for where failures are swallowed and
    /// logged.
    /// </summary>
    public interface IAssistantCacheInvalidator
    {
        Task InvalidateAsync(CancellationToken cancellationToken = default);
    }
}
