namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Mirrors Pat.Aca.BlogServiceApi's CommentStatus string values exactly
    /// (Queued/Unpublished/Published). Deliberately duplicated rather than
    /// referencing that project directly -- this Function is an
    /// independently-deployed process, and taking a project reference to an
    /// ASP.NET Core minimal API just to reuse three string constants would
    /// drag unrelated hosting dependencies into a Functions cold-start path.
    /// The two processes only need to agree on the wire format (the literal
    /// strings written to/read from Cosmos), not share code -- keep these in
    /// sync by hand with CommentStatus.cs if either ever changes.
    /// </summary>
    public static class ModerationCommentStatus
    {
        public const string Queued = "queued";
        public const string Unpublished = "unpublished";
        public const string Published = "published";
    }
}
