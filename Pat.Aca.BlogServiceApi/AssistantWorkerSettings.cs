namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Config for calling the assistant-worker Cloudflare Worker's cache-
    /// invalidation endpoint after a write here changes article content it
    /// might have cached an answer against — bound from the "AssistantWorker"
    /// config section the same plain-POCO-singleton way as CosmosSettings/
    /// CommentSettings, not IOptions&lt;T&gt;. A missing/empty InvalidateCacheUrl
    /// is a valid "not configured" state (e.g. local dev), not an error — see
    /// AssistantCacheInvalidator's own registration in Program.cs.
    /// </summary>
    public sealed class AssistantWorkerSettings
    {
        public string? InvalidateCacheUrl { get; set; }

        /// <summary>
        /// Shared secret sent as the X-Cache-Invalidation-Key header — must
        /// match assistant-worker's own CACHE_INVALIDATION_SECRET, which
        /// fails the request closed if this doesn't match (or is empty).
        /// </summary>
        public string? InvalidateCacheKey { get; set; }
    }
}
