using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IArticleCountPublisher implementation -- POSTs the recomputed
    /// count to api-proxy's own internal endpoint with the shared secret it
    /// expects as X-Article-Count-Sync-Key, api-proxy then writes it into
    /// its ARTICLES_FALLBACK KV namespace itself (via its own binding,
    /// Cloudflare's fast path for a KV write) rather than this Function
    /// calling Cloudflare's external KV REST API directly -- deliberately
    /// mirrors AssistantWorkerCacheInvalidator's exact shape rather than the
    /// CloudflareWorkersAiScorer's direct-external-API-call shape: a KV
    /// write is the same "tell a Worker to do a Cloudflare-native operation"
    /// kind of call as cache invalidation, not a "call out and get an
    /// inference result back" kind of call like Workers AI, so it gets the
    /// same treatment -- one shared secret scoped to exactly this operation,
    /// no new Cloudflare API token to mint/store/rotate.
    /// </summary>
    public sealed class ApiProxyArticleCountPublisher(
        HttpClient httpClient,
        ApiProxySettings settings,
        ILogger<ApiProxyArticleCountPublisher> logger) : IArticleCountPublisher
    {
        private const string SyncKeyHeaderName = "X-Article-Count-Sync-Key";

        public async Task PublishAsync(int count, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.SetArticleCountUrl)
                {
                    Content = JsonContent.Create(new { count })
                };
                request.Headers.Add(SyncKeyHeaderName, settings.ArticleCountSyncSecret);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Article count sync call returned {StatusCode}",
                        response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort by design -- see IArticleCountPublisher's own
                // doc comment for why a failure here must never surface as
                // a failed Change Feed delivery.
                logger.LogWarning(ex, "Article count sync call failed");
            }
        }
    }
}
