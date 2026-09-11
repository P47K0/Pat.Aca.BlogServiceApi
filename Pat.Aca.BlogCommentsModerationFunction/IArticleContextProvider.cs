namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Best-effort lookup of an article's summary, given only its slug.
    /// Exists to give the moderation LLM enough context to actually judge
    /// whether a comment is on-topic -- QueuedComment.Text alone can never
    /// establish that, since the Function otherwise never sees anything
    /// about the article a comment was posted on. Confirmed as a real gap
    /// in production on 2026-09-10/11: an "off-topic spam" judgement on an
    /// innocuous comment, from a model that was never given the article to
    /// compare against in the first place.
    ///
    /// Returns null on any failure (article not found, a transient Cosmos
    /// error). Lookup here is enrichment, not a hard dependency --
    /// CommentModerationProcessor always falls back to scoring on the
    /// comment text alone rather than ever failing moderation over this.
    /// </summary>
    public interface IArticleContextProvider
    {
        Task<string?> GetArticleSummaryAsync(string articleSlug);
    }
}
