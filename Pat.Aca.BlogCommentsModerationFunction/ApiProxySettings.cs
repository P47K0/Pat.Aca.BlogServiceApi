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
        ///
        /// Deliberately reused as-is (not renamed/split) for
        /// SetMostViewedArticleUrl below too -- both are the same trust
        /// relationship (this Function calling its own api-proxy Worker's
        /// internal endpoints), and introducing a second secret here means
        /// redoing the exact three-different-names deployment coordination
        /// (GitHub Actions secret / this app setting / the Worker secret)
        /// that already caused real production debugging pain for the
        /// article-count feature. The name being count-specific is a minor
        /// cosmetic mismatch, not worth that cost.
        /// </summary>
        public string ArticleCountSyncSecret { get; set; } = string.Empty;

        /// <summary>
        /// api-proxy's internal endpoint for the most-viewed-article sync
        /// (see MostViewedSyncFunction), authenticated with the same
        /// ArticleCountSyncSecret above.
        /// </summary>
        public string SetMostViewedArticleUrl { get; set; } = string.Empty;

        /// <summary>
        /// api-proxy's internal endpoint for the latest-articles-list sync
        /// (see ArticleListSyncFunction), authenticated with the same
        /// ArticleCountSyncSecret above -- same reasoning as
        /// SetMostViewedArticleUrl for not minting a third secret.
        /// </summary>
        public string SetArticleListUrl { get; set; } = string.Empty;
    }
}
