namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Tells IndexNow-aware search engines (Bing and others) that URLs
    /// changed. Best-effort like the api-proxy publishers: a failed call
    /// never fails the Change Feed delivery.
    /// </summary>
    public interface IIndexNowPublisher
    {
        Task PingAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken = default);
    }
}
