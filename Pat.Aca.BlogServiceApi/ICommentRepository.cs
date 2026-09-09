namespace Pat.Aca.BlogServiceApi
{
    public interface ICommentRepository
    {
        /// <summary>
        /// Creates a new comment, always landing at CommentStatus.Queued —
        /// the document itself is the "queue entry" for the (not yet built)
        /// Change Feed moderation Function, rather than a separate queue
        /// product. CreatedAt is set here, server-side, never
        /// client-supplied.
        /// </summary>
        Task<Comment> CreateCommentAsync(string articleSlug, CommentWriteRequest request);

        /// <summary>
        /// Public-facing read: only CommentStatus.Published comments,
        /// oldest first — chronological reading order for a conversation,
        /// the opposite of GetArticlesAsync's newest-first, since articles
        /// and comment threads are read differently.
        /// </summary>
        Task<List<Comment>> GetPublishedCommentsAsync(string articleSlug);

        /// <summary>
        /// Moderation-only: every comment regardless of status (including
        /// Email), newest first — surfaces whatever needs review soonest.
        /// Never exposed by a public endpoint.
        /// </summary>
        Task<List<Comment>> GetAllCommentsAsync(string articleSlug);

        /// <summary>
        /// Flips a comment's status (a Comments.Moderate-gated action).
        /// Returns the updated comment, or null if no comment with this id
        /// exists under this article slug.
        /// </summary>
        Task<Comment?> UpdateCommentStatusAsync(string articleSlug, string commentId, string status);

        /// <summary>
        /// Hard-deletes a comment — the one case this container's data is
        /// ever actually removed rather than just marked Unpublished. A
        /// deliberate, human-directed action via Comments.Moderate, never
        /// something the automated moderation flow does itself. Returns
        /// whether a comment was actually deleted.
        /// </summary>
        Task<bool> DeleteCommentAsync(string articleSlug, string commentId);
    }
}
