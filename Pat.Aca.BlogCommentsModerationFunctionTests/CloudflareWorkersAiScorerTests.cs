using System.Net;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class CloudflareWorkersAiScorerTests
    {
        private static CloudflareWorkersAiSettings SampleCloudflareSettings() =>
            new() { AccountId = "test-account", ApiToken = "test-token" };

        // --- ParseModelResponseText: the actual interesting logic, tested directly, no HTTP involved ---

        [Fact]
        public void ParseModelResponseText_parses_plain_json()
        {
            var score = CloudflareWorkersAiScorer.ParseModelResponseText("{\"score\": 4, \"reason\": \"Looks fine.\"}");

            Assert.Equal(4, score.Score);
            Assert.Equal("Looks fine.", score.Reason);
        }

        [Fact]
        public void ParseModelResponseText_strips_a_json_markdown_fence()
        {
            var raw = "```json\n{\"score\": 2, \"reason\": \"Spammy.\"}\n```";

            var score = CloudflareWorkersAiScorer.ParseModelResponseText(raw);

            Assert.Equal(2, score.Score);
            Assert.Equal("Spammy.", score.Reason);
        }

        [Fact]
        public void ParseModelResponseText_strips_a_plain_markdown_fence()
        {
            var raw = "```\n{\"score\": 5, \"reason\": \"Great.\"}\n```";

            var score = CloudflareWorkersAiScorer.ParseModelResponseText(raw);

            Assert.Equal(5, score.Score);
        }

        [Fact]
        public void ParseModelResponseText_fails_safe_to_zero_for_malformed_json()
        {
            var score = CloudflareWorkersAiScorer.ParseModelResponseText("not json at all");

            Assert.Equal(0, score.Score);
            Assert.Contains("Could not parse", score.Reason);
        }

        [Theory]
        [InlineData("{\"score\": 6, \"reason\": \"Too high.\"}")]
        [InlineData("{\"score\": -1, \"reason\": \"Too low.\"}")]
        public void ParseModelResponseText_fails_safe_for_an_out_of_range_score(string raw)
        {
            var score = CloudflareWorkersAiScorer.ParseModelResponseText(raw);

            Assert.Equal(0, score.Score);
        }

        [Fact]
        public void ParseModelResponseText_fails_safe_when_reason_is_missing()
        {
            var score = CloudflareWorkersAiScorer.ParseModelResponseText("{\"score\": 4}");

            Assert.Equal(0, score.Score);
        }

        [Fact]
        public void ParseModelResponseText_truncates_a_very_long_raw_response_in_the_fallback_reason()
        {
            var longRaw = new string('x', 500);

            var score = CloudflareWorkersAiScorer.ParseModelResponseText(longRaw);

            Assert.True(score.Reason.Length < 500);
        }

        // --- ScoreAsync: HTTP wiring, via a fake handler ---

        [Fact]
        public async Task ScoreAsync_sends_the_system_prompt_and_comment_as_separate_messages()
        {
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"result\": {\"response\": \"{\\\"score\\\": 5, \\\"reason\\\": \\\"Fine.\\\"}\"}, \"success\": true}");
            var httpClient = new HttpClient(handler);
            var moderationSettings = new ModerationSettings { ModerationSystemPrompt = "SYSTEM_PROMPT_MARKER" };
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), moderationSettings);

            await scorer.ScoreAsync("A comment to moderate.");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("SYSTEM_PROMPT_MARKER", handler.LastRequestBody);
            Assert.Contains("A comment to moderate.", handler.LastRequestBody);
        }

        [Fact]
        public async Task ScoreAsync_includes_the_bearer_token_and_model_id_in_the_request()
        {
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"result\": {\"response\": \"{\\\"score\\\": 5, \\\"reason\\\": \\\"Fine.\\\"}\"}, \"success\": true}");
            var httpClient = new HttpClient(handler);
            var cloudflareSettings = new CloudflareWorkersAiSettings { AccountId = "acct-123", ApiToken = "secret-token" };
            var moderationSettings = new ModerationSettings { ModerationModelId = "@cf/some/model" };
            var scorer = new CloudflareWorkersAiScorer(httpClient, cloudflareSettings, moderationSettings);

            await scorer.ScoreAsync("A comment.");

            Assert.NotNull(handler.LastRequest);
            Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization?.Parameter);
            Assert.Contains("acct-123", handler.LastRequest.RequestUri!.ToString());
            Assert.Contains("@cf/some/model", handler.LastRequest.RequestUri.ToString());
        }

        [Fact]
        public async Task ScoreAsync_caps_max_tokens_so_the_response_cannot_be_truncated_mid_json()
        {
            // Regression test for a real production incident (2026-09-10):
            // without an explicit cap, Workers AI's own default cut the
            // model's response off mid-JSON ({"score": 3, "reason": "Low"
            // with no closing quote/brace), which correctly failed safe to
            // score 0 -- but at the cost of silently downgrading a real
            // score of 3 down to 0.
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"result\": {\"response\": \"{\\\"score\\\": 5, \\\"reason\\\": \\\"Fine.\\\"}\"}, \"success\": true}");
            var httpClient = new HttpClient(handler);
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), new ModerationSettings());

            await scorer.ScoreAsync("A comment.");

            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("max_tokens", handler.LastRequestBody);
        }

        [Fact]
        public async Task ScoreAsync_returns_the_parsed_score_on_success()
        {
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"result\": {\"response\": \"{\\\"score\\\": 3, \\\"reason\\\": \\\"Borderline.\\\"}\"}, \"success\": true}");
            var httpClient = new HttpClient(handler);
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), new ModerationSettings());

            var score = await scorer.ScoreAsync("A comment.");

            Assert.Equal(3, score.Score);
            Assert.Equal("Borderline.", score.Reason);
        }

        [Fact]
        public async Task ScoreAsync_throws_on_a_non_success_http_status()
        {
            var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "server error");
            var httpClient = new HttpClient(handler);
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), new ModerationSettings());

            await Assert.ThrowsAsync<ModerationScoringException>(() => scorer.ScoreAsync("A comment."));
        }

        [Fact]
        public async Task ScoreAsync_throws_when_cloudflare_reports_success_false()
        {
            var handler = new FakeHttpMessageHandler(
                HttpStatusCode.OK,
                "{\"result\": null, \"success\": false, \"errors\": [{\"code\": 1000, \"message\": \"Invalid model.\"}]}");
            var httpClient = new HttpClient(handler);
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), new ModerationSettings());

            var exception = await Assert.ThrowsAsync<ModerationScoringException>(() => scorer.ScoreAsync("A comment."));
            Assert.Contains("Invalid model.", exception.Message);
        }

        [Fact]
        public async Task ScoreAsync_throws_when_the_http_call_itself_fails()
        {
            var handler = new FakeHttpMessageHandler(new HttpRequestException("DNS failure"));
            var httpClient = new HttpClient(handler);
            var scorer = new CloudflareWorkersAiScorer(httpClient, SampleCloudflareSettings(), new ModerationSettings());

            await Assert.ThrowsAsync<ModerationScoringException>(() => scorer.ScoreAsync("A comment."));
        }
    }
}
