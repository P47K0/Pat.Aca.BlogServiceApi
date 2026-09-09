namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// A reader comment on an article. Unlike Article, there's no legacy Id
    /// baggage here (this is a brand-new container, no pre-existing
    /// hand-authored data to stay compatible with) — Id is simply Cosmos's own
    /// system id, used directly.
    ///
    /// Status drives the moderation flow end to end — see CommentStatus for
    /// the values and what each one means. LlmScore is null until the
    /// moderation Function has actually scored the comment (still Queued).
    /// Email is the one field that must never reach a public-facing response:
    /// it's collected only to let the author be reached/replied to, is
    /// visible solely through the Comments.Moderate endpoints and the
    /// review-notification email, and is never returned by the public
    /// GET /articles/{slug}/comments (that endpoint's response type omits it
    /// entirely rather than just setting it to null on the way out).
    /// </summary>
    public record Comment(
        string Id,
        string ArticleSlug,
        string AuthorName,
        string Text,
        DateTime CreatedAt,
        string Status,
        int? LlmScore = null,
        string? Email = null);
}
