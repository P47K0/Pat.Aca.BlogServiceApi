namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Registered instead of <see cref="AssistantWorkerCacheInvalidator"/>
    /// when AssistantWorker:InvalidateCacheUrl isn't configured (e.g. local
    /// dev, or before the shared secret has been set up) — lets Program.cs's
    /// write endpoints call IAssistantCacheInvalidator unconditionally
    /// without an extra "is this even configured" check at each call site.
    /// </summary>
    public sealed class NoOpAssistantCacheInvalidator : IAssistantCacheInvalidator
    {
        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
