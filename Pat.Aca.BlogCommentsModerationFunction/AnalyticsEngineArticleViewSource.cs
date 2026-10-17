using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Reads the views the ui-worker records in the Analytics Engine dataset
    /// article_referrers (one data point per POST /api/view beacon, blob1 =
    /// slug; see ui-worker lib/referrer-analytics.ts). Counts are
    /// sum(_sample_interval), so they stay right if Analytics Engine samples.
    /// </summary>
    public sealed class AnalyticsEngineArticleViewSource(HttpClient httpClient, CloudflareAnalyticsSettings settings) : IArticleViewSource
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            // Analytics Engine returns sums as numbers or quoted strings
            // depending on the column type.
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public async Task<IReadOnlyDictionary<string, long>> GetViewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var sql = BuildQuery(from, to);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.cloudflare.com/client/v4/accounts/{settings.AccountId}/analytics_engine/sql")
            {
                Content = new StringContent(sql)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Analytics Engine SQL responded {(int)response.StatusCode}: {body}");
            }

            var result = JsonSerializer.Deserialize<SqlResponse>(body, JsonOptions)
                ?? throw new HttpRequestException($"Analytics Engine SQL returned an empty response: {body}");

            var views = new Dictionary<string, long>();
            foreach (var row in result.Data ?? [])
            {
                if (!string.IsNullOrEmpty(row.Slug) && row.Views > 0)
                {
                    views[row.Slug] = (long)Math.Round(row.Views);
                }
            }
            return views;
        }

        /// <summary>
        /// The SQL for [from, to). Only fixed-format timestamps are put into
        /// the string, never anything from the data itself.
        /// </summary>
        public static string BuildQuery(DateTime from, DateTime to) => $"""
            SELECT blob1 AS slug, sum(_sample_interval) AS views
            FROM article_referrers
            WHERE timestamp >= toDateTime('{Format(from)}') AND timestamp < toDateTime('{Format(to)}')
            GROUP BY slug
            """;

        private static string Format(DateTime value) =>
            DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private sealed class SqlResponse
        {
            public List<SqlRow>? Data { get; set; }
        }

        private sealed class SqlRow
        {
            public string? Slug { get; set; }
            public double Views { get; set; }
        }
    }
}
