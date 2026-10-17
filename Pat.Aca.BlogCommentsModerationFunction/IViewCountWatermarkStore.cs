namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// How far ViewCountFlushProcessor has counted: every view before
    /// <see cref="ViewCountWatermark.CountedUntil"/> is already in Cosmos.
    /// </summary>
    public sealed record ViewCountWatermark(DateTime CountedUntil, string ETag);

    public interface IViewCountWatermarkStore
    {
        Task<ViewCountWatermark?> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>Creates the watermark on the first run. False when another run created it first.</summary>
        Task<bool> TryCreateAsync(DateTime countedUntil, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the watermark, only if it is still the one read (its
        /// ETag). False when another run moved it in between.
        /// </summary>
        Task<bool> TryAdvanceAsync(ViewCountWatermark current, DateTime countedUntil, CancellationToken cancellationToken = default);
    }
}
