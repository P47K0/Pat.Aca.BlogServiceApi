namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Scores one comment's text for moderation, optionally given the
    /// article's summary for context (see IArticleContextProvider's own
    /// doc comment for why that matters -- without it, "is this on-topic"
    /// isn't something the model can actually judge). articleSummary is
    /// null when the lookup failed or the article has none; implementations
    /// fall back to scoring on commentText alone in that case, not fail.
    /// The real implementation calls Cloudflare Workers AI over plain HTTP;
    /// kept behind this interface so CommentModerationProcessor's decision
    /// logic is testable without a real network call or a Cloudflare API
    /// token.
    /// </summary>
    public interface IModerationScorer
    {
        Task<ModerationScore> ScoreAsync(string commentText, string? articleSummary);
    }
}
