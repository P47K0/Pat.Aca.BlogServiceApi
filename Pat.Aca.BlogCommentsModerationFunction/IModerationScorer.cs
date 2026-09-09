namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Scores one comment's text for moderation. The real implementation
    /// (a later commit) calls Cloudflare Workers AI over plain HTTP; kept
    /// behind this interface so CommentModerationProcessor's decision logic
    /// is testable without a real network call or a Cloudflare API token.
    /// </summary>
    public interface IModerationScorer
    {
        Task<ModerationScore> ScoreAsync(string commentText);
    }
}
