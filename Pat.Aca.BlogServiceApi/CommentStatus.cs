namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// The values Comment.Status moves through. A comment is written as
    /// Queued the instant it's submitted (the document itself is the "queue
    /// entry" — see the Comments Cosmos container's Change Feed, consumed by
    /// a separate moderation Function, not a dedicated queue product).
    /// Nothing is ever deleted by the automated flow — Unpublished is a
    /// resting state for manual review, not a rejection; only a human
    /// (via the Comments.Moderate endpoints) can move a comment out of it,
    /// either by publishing it or by hard-deleting it outright as spam.
    ///
    /// Today, the moderation Function always lands a scored comment on
    /// Unpublished regardless of its LlmScore — a human always makes the
    /// final publish call. An AUTO_PUBLISH_MIN_SCORE Function App setting is
    /// planned to let a high-scoring comment go straight to Published
    /// instead, once that's trusted enough to turn on; that's a
    /// configuration change in the Function, not a new status value.
    /// </summary>
    public static class CommentStatus
    {
        public const string Queued = "queued";
        public const string Unpublished = "unpublished";
        public const string Published = "published";
    }
}
