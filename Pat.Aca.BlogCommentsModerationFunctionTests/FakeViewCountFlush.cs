using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeArticleViewSource(IReadOnlyDictionary<string, long> views) : IArticleViewSource
    {
        public List<(DateTime From, DateTime To)> Queries { get; } = [];

        public Task<IReadOnlyDictionary<string, long>> GetViewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            Queries.Add((from, to));
            return Task.FromResult(views);
        }
    }

    public sealed class FakeViewCountWatermarkStore : IViewCountWatermarkStore
    {
        public ViewCountWatermark? Current { get; set; }

        /// <summary>Makes the next TryAdvanceAsync lose the race to another run.</summary>
        public bool LoseNextAdvance { get; set; }

        private int _version;

        public Task<ViewCountWatermark?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task<bool> TryCreateAsync(DateTime countedUntil, CancellationToken cancellationToken = default)
        {
            if (Current is not null) return Task.FromResult(false);
            Current = new ViewCountWatermark(countedUntil, $"etag-{++_version}");
            return Task.FromResult(true);
        }

        public Task<bool> TryAdvanceAsync(ViewCountWatermark current, DateTime countedUntil, CancellationToken cancellationToken = default)
        {
            if (LoseNextAdvance || Current?.ETag != current.ETag) return Task.FromResult(false);
            Current = new ViewCountWatermark(countedUntil, $"etag-{++_version}");
            return Task.FromResult(true);
        }
    }

    public sealed class FakeArticleViewCountWriter(params string[] knownSlugs) : IArticleViewCountWriter
    {
        public Dictionary<string, long> Added { get; } = [];
        public HashSet<string> Failing { get; } = [];

        public Task<bool> AddViewsAsync(string slug, long views, CancellationToken cancellationToken = default)
        {
            if (Failing.Contains(slug)) throw new InvalidOperationException("Cosmos down");
            if (!knownSlugs.Contains(slug)) return Task.FromResult(false);
            Added[slug] = Added.GetValueOrDefault(slug) + views;
            return Task.FromResult(true);
        }
    }
}
