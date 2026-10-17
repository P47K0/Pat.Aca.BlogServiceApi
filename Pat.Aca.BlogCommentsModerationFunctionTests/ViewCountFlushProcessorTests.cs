using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class ViewCountFlushProcessorTests
    {
        private static readonly DateTime Now = new(2026, 10, 17, 12, 10, 30, DateTimeKind.Utc);
        private static readonly DateTime CountedUntil = new(2026, 10, 17, 12, 5, 0, DateTimeKind.Utc);
        private static readonly DateTime PreviousRun = new(2026, 10, 17, 11, 5, 0, DateTimeKind.Utc);

        private static readonly CloudflareAnalyticsSettings Configured = new() { AccountId = "account", ApiToken = "token" };

        private static ViewCountFlushProcessor Processor(
            FakeArticleViewSource source,
            FakeViewCountWatermarkStore store,
            FakeArticleViewCountWriter writer,
            CloudflareAnalyticsSettings? settings = null) =>
            new(source, store, writer, settings ?? Configured, NullLogger<ViewCountFlushProcessor>.Instance);

        [Fact]
        public async Task First_run_only_sets_the_watermark()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long> { ["a"] = 3 });
            var store = new FakeViewCountWatermarkStore();
            var writer = new FakeArticleViewCountWriter("a");

            await Processor(source, store, writer).FlushAsync(Now);

            Assert.Equal(CountedUntil, store.Current?.CountedUntil);
            Assert.Empty(source.Queries);
            Assert.Empty(writer.Added);
        }

        [Fact]
        public async Task Adds_the_views_since_the_watermark_and_moves_it()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long> { ["a"] = 3, ["b"] = 1 });
            var store = new FakeViewCountWatermarkStore { Current = new ViewCountWatermark(PreviousRun, "etag-0") };
            var writer = new FakeArticleViewCountWriter("a", "b");

            await Processor(source, store, writer).FlushAsync(Now);

            Assert.Equal([(PreviousRun, CountedUntil)], source.Queries);
            Assert.Equal(new Dictionary<string, long> { ["a"] = 3, ["b"] = 1 }, writer.Added);
            Assert.Equal(CountedUntil, store.Current?.CountedUntil);
        }

        [Fact]
        public async Task Adds_nothing_when_another_run_moved_the_watermark_first()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long> { ["a"] = 3 });
            var store = new FakeViewCountWatermarkStore
            {
                Current = new ViewCountWatermark(PreviousRun, "etag-0"),
                LoseNextAdvance = true
            };
            var writer = new FakeArticleViewCountWriter("a");

            await Processor(source, store, writer).FlushAsync(Now);

            Assert.Empty(writer.Added);
        }

        [Fact]
        public async Task A_failing_article_does_not_stop_the_others()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long> { ["a"] = 3, ["b"] = 2, ["gone"] = 1 });
            var store = new FakeViewCountWatermarkStore { Current = new ViewCountWatermark(PreviousRun, "etag-0") };
            var writer = new FakeArticleViewCountWriter("a", "b");
            writer.Failing.Add("a");

            await Processor(source, store, writer).FlushAsync(Now);

            Assert.Equal(new Dictionary<string, long> { ["b"] = 2 }, writer.Added);
            Assert.Equal(CountedUntil, store.Current?.CountedUntil);
        }

        [Fact]
        public async Task An_old_watermark_only_counts_what_Analytics_Engine_still_keeps()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long>());
            var store = new FakeViewCountWatermarkStore { Current = new ViewCountWatermark(CountedUntil.AddDays(-200), "etag-0") };

            await Processor(source, store, new FakeArticleViewCountWriter()).FlushAsync(Now);

            Assert.Equal([(CountedUntil - ViewCountFlushProcessor.MaxWindow, CountedUntil)], source.Queries);
        }

        [Fact]
        public async Task Does_nothing_while_the_window_is_empty()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long>());
            var store = new FakeViewCountWatermarkStore { Current = new ViewCountWatermark(CountedUntil, "etag-0") };

            await Processor(source, store, new FakeArticleViewCountWriter()).FlushAsync(Now);

            Assert.Empty(source.Queries);
            Assert.Equal("etag-0", store.Current?.ETag);
        }

        [Fact]
        public async Task Does_nothing_without_Analytics_Engine_credentials()
        {
            var source = new FakeArticleViewSource(new Dictionary<string, long>());
            var store = new FakeViewCountWatermarkStore();

            await Processor(source, store, new FakeArticleViewCountWriter(), new CloudflareAnalyticsSettings()).FlushAsync(Now);

            Assert.Null(store.Current);
        }

        [Fact]
        public void Query_filters_on_the_window_in_utc()
        {
            var sql = AnalyticsEngineArticleViewSource.BuildQuery(PreviousRun, CountedUntil);

            Assert.Contains("timestamp >= toDateTime('2026-10-17 11:05:00')", sql);
            Assert.Contains("timestamp < toDateTime('2026-10-17 12:05:00')", sql);
            Assert.Contains("sum(_sample_interval)", sql);
        }

        [Fact]
        public async Task Source_reads_views_per_slug_from_the_sql_response()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
                """{"meta":[],"data":[{"slug":"a","views":"3"},{"slug":"b","views":1}],"rows":2}""");
            var source = new AnalyticsEngineArticleViewSource(new HttpClient(handler), Configured);

            var views = await source.GetViewsAsync(PreviousRun, CountedUntil);

            Assert.Equal(new Dictionary<string, long> { ["a"] = 3, ["b"] = 1 }, views);
            Assert.Equal("https://api.cloudflare.com/client/v4/accounts/account/analytics_engine/sql", handler.LastRequest?.RequestUri?.ToString());
            Assert.Equal("Bearer token", handler.LastRequest?.Headers.Authorization?.ToString());
        }

        [Fact]
        public async Task Source_throws_on_an_error_response()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, "bad token");
            var source = new AnalyticsEngineArticleViewSource(new HttpClient(handler), Configured);

            await Assert.ThrowsAsync<HttpRequestException>(() => source.GetViewsAsync(PreviousRun, CountedUntil));
        }
    }
}
