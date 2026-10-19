using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IPublishedCommentsPublisher: POSTs to api-proxy's
    /// /internal/comments with the shared sync secret (URL derived like
    /// ApiProxyArticleDetailPublisher's).
    /// </summary>
    public sealed class ApiProxyPublishedCommentsPublisher(
        HttpClient httpClient,
        ApiProxySettings settings,
        ILogger<ApiProxyPublishedCommentsPublisher> logger) : IPublishedCommentsPublisher
    {
        private const string SyncKeyHeaderName = "X-Article-Count-Sync-Key";

        public async Task PublishAsync(IReadOnlyDictionary<string, List<PublishedComment>> commentsBySlug, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiProxyInternalUrl("/internal/comments"))
                {
                    Content = JsonContent.Create(new
                    {
                        comments = commentsBySlug.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.Select(comment => new
                            {
                                id = comment.Id,
                                authorName = comment.AuthorName,
                                text = comment.Text,
                                createdAt = comment.CreatedAt,
                            })),
                    })
                };
                request.Headers.Add(SyncKeyHeaderName, settings.ArticleCountSyncSecret);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Comments sync call returned {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Comments sync call failed");
            }
        }
    }
}
