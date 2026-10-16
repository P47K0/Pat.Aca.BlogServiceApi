using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Recompute every published comment and push the whole set, like
    /// ArticleListSyncProcessor does for the list: the set is small, and
    /// recomputing means a status change, a new comment and (via the timer)
    /// a delete all end up right without reasoning about deltas.
    /// </summary>
    public sealed class CommentsSyncProcessor(
        IPublishedCommentsRepository repository,
        IPublishedCommentsPublisher publisher,
        ILogger<CommentsSyncProcessor> logger)
    {
        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            var commentsBySlug = await repository.GetPublishedCommentsBySlugAsync(cancellationToken);
            logger.LogInformation(
                "Recomputed published comments for {Count} article(s). Publishing to api-proxy.", commentsBySlug.Count);
            await publisher.PublishAsync(commentsBySlug, cancellationToken);
        }
    }
}
