namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// One published comment as api-proxy serves it: the same fields as the
    /// API's PublicComment (no email, status or llmScore).
    /// </summary>
    public sealed record PublishedComment(string Id, string AuthorName, string Text, DateTime CreatedAt);
}
