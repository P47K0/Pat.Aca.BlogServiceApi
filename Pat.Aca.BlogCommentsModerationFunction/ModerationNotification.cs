namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Everything IModerationNotifier needs to compose the review-alert
    /// email -- sent for every scored comment, not just the ones needing
    /// review: AutoPublished distinguishes "FYI, this went live
    /// automatically, flag it if the LLM got it wrong" from "awaiting your
    /// review", per the design's always-notify-either-way decision.
    /// </summary>
    public record ModerationNotification(
        string CommentId,
        string ArticleSlug,
        string AuthorName,
        string CommentText,
        string? Email,
        int Score,
        string Reason,
        bool AutoPublished);
}
