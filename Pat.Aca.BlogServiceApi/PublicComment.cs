namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// The shape returned by the public GET /articles/{slug}/comments —
    /// deliberately narrower than Comment: no Email (this container's one
    /// piece of reader PII, never meant for public consumption — see
    /// CommentDocument's doc comment), no Status (every comment returned
    /// here is already CommentStatus.Published, so it'd be redundant), no
    /// LlmScore (an internal moderation detail), and no ArticleSlug (the
    /// caller already knows it — it's in the request URL).
    /// </summary>
    public record PublicComment(string Id, string AuthorName, string Text, DateTime CreatedAt)
    {
        public static PublicComment FromComment(Comment comment) =>
            new(comment.Id, comment.AuthorName, comment.Text, comment.CreatedAt);
    }
}
