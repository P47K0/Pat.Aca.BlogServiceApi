using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pure orchestration behind MostViewedSyncFunction, decoupled from the
    /// TimerTrigger binding for testability -- same split as
    /// ArticleCountSyncProcessor vs. ArticleCountSyncFunction. Skips
    /// publishing entirely when there's no eligible article at all (an
    /// empty or fully-unlisted blog), rather than pushing a null/garbage
    /// value -- api-proxy just keeps serving whatever it last had (or
    /// nothing, before the very first successful run).
    /// </summary>
    public sealed class MostViewedSyncProcessor(
        IMostViewedArticleRepository articleRepository,
        IMostViewedArticlePublisher articlePublisher,
        ILogger<MostViewedSyncProcessor> logger)
    {
        public async Task SyncAsync(CancellationToken cancellationToken = default)
        {
            var mostViewed = await articleRepository.GetMostViewedArticleAsync(cancellationToken);
            if (mostViewed is null)
            {
                logger.LogInformation("No eligible article found for the most-viewed sync -- skipping publish.");
                return;
            }

            logger.LogInformation(
                "Most-viewed article: {Slug} ({ViewCount} views). Publishing to api-proxy.",
                mostViewed.Slug,
                mostViewed.ViewCount);
            await articlePublisher.PublishAsync(mostViewed, cancellationToken);
        }
    }
}
