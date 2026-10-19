using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleDetailPublisher: POSTs to api-proxy's
    /// /internal/article-detail with the shared sync secret, best-effort
    /// like ApiProxyArticleListPublisher. The URL is derived from
    /// SetArticleListUrl (same Worker), so this needs no new app setting
    /// and no infra workflow run.
    /// </summary>
    public sealed class ApiProxyArticleDetailPublisher(
        HttpClient httpClient,
        ApiProxySettings settings,
        ILogger<ApiProxyArticleDetailPublisher> logger) : IArticleDetailPublisher
    {
        private const string SyncKeyHeaderName = "X-Article-Count-Sync-Key";

        public async Task PublishAsync(ArticleDetailChangeFeedDocument article, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiProxyInternalUrl("/internal/article-detail"))
                {
                    Content = JsonContent.Create(new
                    {
                        slug = article.Slug,
                        title = article.Title,
                        summary = article.Summary,
                        content = article.Content,
                        publishedAt = article.PublishedAt,
                        tags = article.Tags,
                        viewCount = article.ViewCount,
                        linkedinVideoEmbedUrl = article.LinkedinVideoEmbedUrl,
                        seriesName = article.SeriesName,
                        seriesOrder = article.SeriesOrder,
                        relatedSlugs = article.RelatedSlugs,
                        unlisted = article.Unlisted,
                        coverImageUrl = article.CoverImageUrl,
                        seoDescription = article.SeoDescription,
                        seoKeywords = article.SeoKeywords,
                        updatedAt = article.UpdatedAt,
                    })
                };
                request.Headers.Add(SyncKeyHeaderName, settings.ArticleCountSyncSecret);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Article detail sync call for {Slug} returned {StatusCode}", article.Slug, response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Article detail sync call for {Slug} failed", article.Slug);
            }
        }
    }
}
