namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Body for PATCH /articles/{slug}/comments/{commentId} — the
    /// Comments.Moderate-gated status flip. Status must be
    /// CommentStatus.Published or CommentStatus.Unpublished;
    /// CommentStatus.Queued is a system-only transient state and
    /// deliberately not a valid target here — nothing manually re-queues a
    /// comment.
    /// </summary>
    public record CommentStatusUpdateRequest(string Status);
}
