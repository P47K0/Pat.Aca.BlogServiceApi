using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Keeps Cosmos viewCount (and so the homepage's most-viewed article)
    /// moving now that article reads no longer reach the API: adds the views
    /// the ui-worker's page beacon recorded in Analytics Engine since the
    /// last run, straight to Cosmos. No call to the API, so it never wakes
    /// the container.
    ///
    /// Counts the window [watermark, now - IngestDelay): Analytics Engine
    /// takes a little while to make a data point queryable, so the newest
    /// minutes wait for the next run. The watermark moves (conditionally, on
    /// its ETag) before the increments are applied, so two overlapping runs
    /// can't both count the same window, and a failed increment is a lost
    /// batch for that article, never a double count. The same accepted
    /// undercount as the old per-read increment.
    ///
    /// The first run only sets the watermark: until this ships, the API
    /// counted every origin read itself, and counting the beacon's history
    /// on top of that would count those views twice.
    /// </summary>
    public sealed class ViewCountFlushProcessor(
        IArticleViewSource viewSource,
        IViewCountWatermarkStore watermarkStore,
        IArticleViewCountWriter viewCountWriter,
        CloudflareAnalyticsSettings analyticsSettings,
        ILogger<ViewCountFlushProcessor> logger)
    {
        public static readonly TimeSpan IngestDelay = TimeSpan.FromMinutes(5);

        // Analytics Engine keeps three months; a watermark older than this
        // (the Function was off for that long) only counts what is still there.
        public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(89);

        public async Task FlushAsync(DateTime utcNow, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(analyticsSettings.AccountId) || string.IsNullOrEmpty(analyticsSettings.ApiToken))
            {
                logger.LogWarning("CloudflareAnalytics settings are missing; article views are not being added to viewCount.");
                return;
            }

            var until = TruncateToMinute(utcNow - IngestDelay);
            var watermark = await watermarkStore.GetAsync(cancellationToken);
            if (watermark is null)
            {
                if (await watermarkStore.TryCreateAsync(until, cancellationToken))
                {
                    logger.LogInformation("First run: counting article views from {Until:o} on.", until);
                }
                return;
            }

            var from = watermark.CountedUntil < until - MaxWindow ? until - MaxWindow : watermark.CountedUntil;
            if (from >= until)
            {
                return;
            }

            var views = await viewSource.GetViewsAsync(from, until, cancellationToken);

            if (!await watermarkStore.TryAdvanceAsync(watermark, until, cancellationToken))
            {
                logger.LogInformation("Another run already counted the views from {From:o}; skipping.", from);
                return;
            }

            var added = 0L;
            foreach (var (slug, count) in views)
            {
                try
                {
                    if (await viewCountWriter.AddViewsAsync(slug, count, cancellationToken))
                    {
                        added += count;
                    }
                    else
                    {
                        logger.LogWarning("No article with slug {Slug}; dropped {Count} view(s).", slug, count);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not add {Count} view(s) to {Slug}; they are lost.", count, slug);
                }
            }

            logger.LogInformation(
                "Added {Added} view(s) over {Articles} article(s) for {From:o} to {Until:o}.",
                added, views.Count, from, until);
        }

        private static DateTime TruncateToMinute(DateTime value) =>
            new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }
}
