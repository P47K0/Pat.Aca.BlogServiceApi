namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Real implementation of <see cref="IAssistantCacheInvalidator"/> —
    /// calls assistant-worker's POST /internal/invalidate-cache with the
    /// shared secret it expects as X-Cache-Invalidation-Key. Registered
    /// only when AssistantWorker:InvalidateCacheUrl is actually configured
    /// (see Program.cs); <see cref="NoOpAssistantCacheInvalidator"/> covers
    /// the unconfigured case (e.g. local dev), the same
    /// configured-vs-not branching CosmosSettings already uses for
    /// IArticleRepository.
    /// </summary>
    public sealed class AssistantWorkerCacheInvalidator(
        HttpClient httpClient,
        AssistantWorkerSettings settings,
        ILogger<AssistantWorkerCacheInvalidator> logger) : IAssistantCacheInvalidator
    {
        private const string InvalidationKeyHeaderName = "X-Cache-Invalidation-Key";

        public async Task InvalidateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.InvalidateCacheUrl);
                request.Headers.Add(InvalidationKeyHeaderName, settings.InvalidateCacheKey);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Assistant cache invalidation call returned {StatusCode}",
                        response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort by design — see the interface doc comment for
                // why a failure here must never surface as a failed write.
                logger.LogWarning(ex, "Assistant cache invalidation call failed");
            }
        }
    }
}
