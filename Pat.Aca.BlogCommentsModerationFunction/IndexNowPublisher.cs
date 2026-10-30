using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IIndexNowPublisher: one POST to the shared IndexNow endpoint
    /// (which forwards to every participating engine) with the whole URL
    /// list. 200 and 202 both mean accepted.
    /// </summary>
    public sealed class IndexNowPublisher(
        HttpClient httpClient,
        IndexNowSettings settings,
        ILogger<IndexNowPublisher> logger) : IIndexNowPublisher
    {
        public async Task PingAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync(
                    settings.Endpoint,
                    new { host = settings.Host, key = settings.Key, keyLocation = settings.KeyLocation, urlList = urls },
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("IndexNow accepted {Count} URL(s) with {StatusCode}.", urls.Count, (int)response.StatusCode);
                }
                else
                {
                    logger.LogWarning("IndexNow ping for {Count} URL(s) returned {StatusCode}", urls.Count, response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "IndexNow ping for {Count} URL(s) failed", urls.Count);
            }
        }
    }
}
