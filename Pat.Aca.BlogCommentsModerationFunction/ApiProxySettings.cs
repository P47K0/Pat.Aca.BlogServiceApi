namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Config for calling api-proxy's internal blog-post-count-sync
    /// endpoint after a recompute -- bound from the "ApiProxy" config
    /// section the same plain-POCO-singleton way as every other settings
    /// class here (not IOptions&lt;T&gt;). Mirrors the sibling API project's
    /// AssistantWorkerSettings shape exactly (Url + shared-secret pair for
    /// one Worker's one internal endpoint), just pointed at a different
    /// Worker (api-proxy, not assistant-worker) for a different purpose (set
    /// the durable KV count, not invalidate a cache).
    /// </summary>
    public sealed class ApiProxySettings
    {
        public string SetArticleCountUrl { get; set; } = string.Empty;

        /// <summary>
        /// Shared secret sent as the X-Article-Count-Sync-Key header --
        /// must match api-proxy's own ARTICLE_COUNT_SYNC_SECRET, which fails
        /// the request closed if this doesn't match (or is empty).
        /// </summary>
        public string ArticleCountSyncSecret { get; set; } = string.Empty;
    }
}
