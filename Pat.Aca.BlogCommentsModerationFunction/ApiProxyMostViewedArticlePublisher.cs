using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IMostViewedArticlePublisher implementation -- same shape as
    /// ApiProxyArticleCountPublisher (internal Worker endpoint, shared
    /// secret header, best-effort), just a different URL and payload.
    /// Deliberately reuses ApiProxySettings.ArticleCountSyncSecret rather
    /// than a second secret -- see that property's own doc comment.
    /// </summary>
    public sealed class ApiProxyMostViewedArticlePublisher(
        HttpClient httpClient,
        ApiProxySettings settings,
        ILogger<ApiProxyMostViewedArticlePublisher> logger) : IMostViewedArticlePublisher
    {
        private const string SyncKeyHeaderName = "X-Article-Count-Sync-Key";

        public async Task PublishAsync(MostViewedArticleResult article, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.SetMostViewedArticleUrl)
                {
                    Content = JsonContent.Create(new
                    {
                        slug = article.Slug,
                        title = article.Title,
                        summary = article.Summary,
                        viewCount = article.ViewCount,
                    })
                };
                request.Headers.Add(SyncKeyHeaderName, settings.ArticleCountSyncSecret);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Most-viewed-article sync call returned {StatusCode}",
                        response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Most-viewed-article sync call failed");
            }
        }
    }
}
