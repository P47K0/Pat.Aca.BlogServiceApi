namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Config for the IndexNow ping (see IndexNowSyncFunction). The key is
    /// public by design: IndexNow proves ownership by fetching it from
    /// KeyLocation, so it is a default here rather than an app setting. It
    /// must match IndexNow key in ui-worker's src/index.tsx, which serves the
    /// key file.
    /// </summary>
    public sealed class IndexNowSettings
    {
        public string Endpoint { get; set; } = "https://api.indexnow.org/indexnow";

        public string Host { get; set; } = "blog.koorevaar.com";

        public string Key { get; set; } = "e6404dec318e412daad2061f7710859f";

        public string KeyLocation => $"https://{Host}/{Key}.txt";

        /// <summary>
        /// How recent an article's updatedAt (publishedAt when never edited)
        /// must be for its Change Feed write to count as a reader-visible
        /// change. The hourly view-count flush rewrites documents without
        /// touching updatedAt, so a write whose updatedAt is old is a flush.
        /// </summary>
        public TimeSpan ChangeWindow { get; set; } = TimeSpan.FromMinutes(15);
    }
}
