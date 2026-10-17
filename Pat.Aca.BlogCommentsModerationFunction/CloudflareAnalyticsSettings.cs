namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Credentials for the Analytics Engine SQL API
    /// (api.cloudflare.com/client/v4/accounts/{AccountId}/analytics_engine/sql),
    /// which ViewCountFlushProcessor reads the ui-worker's article views
    /// from. A separate token from CloudflareWorkersAiSettings' one: this one
    /// only needs Account Analytics Read. Left empty, the flush logs a
    /// warning and does nothing.
    /// </summary>
    public sealed class CloudflareAnalyticsSettings
    {
        public string AccountId { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
    }
}
