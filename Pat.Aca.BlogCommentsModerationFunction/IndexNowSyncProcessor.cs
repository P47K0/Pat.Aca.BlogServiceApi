using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Pure orchestration behind IndexNowSyncFunction, decoupled from the
    /// trigger binding for testability (same split as
    /// ArticleDetailSyncProcessor). Picks the articles whose write changed
    /// what readers see and sends one IndexNow request for the whole batch.
    ///
    /// Skipped: unlisted articles (the about page), future-dated ones (the
    /// article 404s until publishedAt), and writes whose updatedAt
    /// (publishedAt when never edited) is older than the change window,
    /// which are the view-count flush. Scheduled articles that go live
    /// cause no write, so they are not pinged; the next crawl finds them.
    /// </summary>
    public sealed class IndexNowSyncProcessor(
        IIndexNowPublisher publisher,
        IndexNowSettings settings,
        ILogger<IndexNowSyncProcessor> logger)
    {
        public async Task SyncAsync(IReadOnlyList<ArticleDetailChangeFeedDocument> changes, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var slugs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var change in changes)
            {
                if (IsReaderVisibleChange(change, nowUtc))
                {
                    slugs.Add(change.Slug);
                }
            }

            if (slugs.Count == 0)
            {
                logger.LogInformation("No reader-visible article change in {Count} change(s), skipping IndexNow.", changes.Count);
                return;
            }

            // The home page lists the article, so it changed too.
            var urls = new List<string> { $"https://{settings.Host}/" };
            urls.AddRange(slugs.Select(slug => $"https://{settings.Host}/articles/{Uri.EscapeDataString(slug)}"));

            await publisher.PingAsync(urls, cancellationToken);
        }

        private bool IsReaderVisibleChange(ArticleDetailChangeFeedDocument change, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(change.Slug) || change.Unlisted)
            {
                return false;
            }

            if (!TryParseUtc(change.PublishedAt, out var publishedAt) || publishedAt > nowUtc)
            {
                return false;
            }

            var lastEdit = TryParseUtc(change.UpdatedAt, out var updatedAt) && updatedAt > publishedAt
                ? updatedAt
                : publishedAt;
            return nowUtc - lastEdit <= settings.ChangeWindow;
        }

        private static bool TryParseUtc(string? value, out DateTime utc) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
    }
}
