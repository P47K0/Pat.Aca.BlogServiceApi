namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The subset of a Comments-container document this Function actually
    /// needs, read off a Cosmos DB Change Feed trigger payload for a
    /// newly-queued comment (see ModerationCommentStatus.Queued). Deliberately
    /// narrower than the full Cosmos document shape -- CreatedAt/Status
    /// aren't needed by the moderation decision itself.
    /// </summary>
    public record QueuedComment(string Id, string ArticleSlug, string AuthorName, string Text, string? Email = null);
}
