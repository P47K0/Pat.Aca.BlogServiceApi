namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Thrown by IModerationScorer implementations for genuine
    /// infrastructure failures -- can't reach Cloudflare at all, a
    /// non-success HTTP status, or Cloudflare's own envelope reporting
    /// "success": false. Deliberately distinct from a merely unparseable
    /// model response (see CloudflareWorkersAiScorer's own doc comment on
    /// why that case fails safe to a low score instead of throwing): an
    /// infra failure means the model never actually ran, so there's nothing
    /// to fail safe to -- retrying later is the right instinct, which is
    /// exactly what letting this propagate out of
    /// CommentModerationProcessor.ProcessAsync (per its own doc comment)
    /// enables the caller to decide on.
    /// </summary>
    public sealed class ModerationScoringException : Exception
    {
        public ModerationScoringException(string message) : base(message)
        {
        }

        public ModerationScoringException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
