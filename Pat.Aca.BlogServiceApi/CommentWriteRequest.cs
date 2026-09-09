namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Request body for POST /articles/{slug}/comments — submitted by an
    /// anonymous reader, unlike ArticleWriteRequest (AI-caller-only, behind
    /// Azure AD). ArticleSlug isn't part of this type because it comes from
    /// the route, not the body. Id/CreatedAt/Status/LlmScore are all
    /// server-owned and deliberately absent here, same reasoning as
    /// ArticleWriteRequest excluding Id/ViewCount — a reader submits only
    /// what a comment actually is, not its moderation state.
    ///
    /// AuthorName and Text are required (enforced by CommentWriteValidation,
    /// against length caps from CommentSettings — not hardcoded, so they can
    /// be tuned without a code change). Email is optional and, if given, is
    /// only checked for looking like a real address — no confirmation/
    /// verification flow. Callers should pass this type through
    /// CommentWriteValidation.Trim first: leading/trailing whitespace is not
    /// trimmed by this record itself.
    /// </summary>
    public record CommentWriteRequest(
        string AuthorName,
        string Text,
        string? Email = null);
}
