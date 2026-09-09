namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The LLM's moderation verdict for one comment. Score is 0-5 (0 = do
    /// not publish, 5 = ok to publish) per the original design spec --
    /// offensive/sexual content, commercial/ads, and garbage should all
    /// score low. Reason is a brief human-readable explanation, surfaced in
    /// the review-notification email so a human can sanity-check the score
    /// without re-reading the whole comment from scratch.
    /// </summary>
    public record ModerationScore(int Score, string Reason);
}
