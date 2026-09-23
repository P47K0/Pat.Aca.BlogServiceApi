using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleListPublisher implementation -- same shape as
    /// ApiProxyArticleCountPublisher/ApiProxyMostViewedArticlePublisher
    /// (internal Worker endpoint, shared secret header, best-effort), just a
    /// different URL and an array payload. Deliberately reuses
    /// ApiProxySettings.ArticleCountSyncSecret rather than a second secret --
    /// see that property's own doc comment.
    /// </summary>
    public sealed class ApiProxyArticleListPublisher(
        HttpClient httpClient,
        ApiProxySettings settings,
        ILogger<ApiProxyArticleListPublisher> logger) : IArticleListPublisher
    {
        private const string SyncKeyHeaderName = "X-Article-Count-Sync-Key";

        public async Task PublishAsync(IReadOnlyList<ArticleListItem> articles, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.SetArticleListUrl)
                {
                    Content = JsonContent.Create(new
                    {
                        articles = articles.Select(article => new
                        {
                            id = article.Id,
                            slug = article.Slug,
                            title = article.Title,
                            summary = article.Summary,
                            publishedAt = article.PublishedAt,
                            tags = article.Tags,
                            viewCount = article.ViewCount,
                            linkedinVideoEmbedUrl = article.LinkedinVideoEmbedUrl,
                        }),
                    })
                };
                request.Headers.Add(SyncKeyHeaderName, settings.ArticleCountSyncSecret);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Article list sync call returned {StatusCode}",
                        response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort by design -- see IArticleListPublisher's own
                // doc comment for why a failure here must never surface as
                // a failed Change Feed delivery.
                logger.LogWarning(ex, "Article list sync call failed");
            }
        }
    }
}
