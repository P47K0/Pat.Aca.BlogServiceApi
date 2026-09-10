using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IModerationScorer implementation: calls Cloudflare Workers AI
    /// directly over plain HTTP (api.cloudflare.com/client/v4/accounts/
    /// {AccountId}/ai/run/{ModerationModelId}) -- no Cloudflare SDK needed,
    /// this works the same from anywhere, not just from inside a Worker.
    /// ModerationSettings.ModerationSystemPrompt is sent as the system
    /// message, the comment's raw text as a separate user message.
    ///
    /// Two distinct failure modes, handled differently on purpose:
    /// - Genuine infrastructure failure (can't reach Cloudflare, non-success
    ///   HTTP status, or the response envelope's own "success": false) --
    ///   the model never actually ran, so there's nothing to fail safe to.
    ///   Throws ModerationScoringException, which CommentModerationProcessor
    ///   deliberately lets propagate (per its own doc comment) so the
    ///   eventual caller can decide whether to retry or leave the comment
    ///   Queued.
    /// - The model DID run, but its response text doesn't parse into the
    ///   {"score": 0-5, "reason": "..."} shape the prompt asks for (bad
    ///   JSON, missing fields, an out-of-range score) -- ParseModelResponseText
    ///   fails safe to ModerationScore(0, ...) instead of throwing. A
    ///   formatting quirk in the model's output isn't an infra problem
    ///   retrying would fix, and defaulting to the lowest score (rather
    ///   than silently dropping the comment, or worse, guessing it's fine)
    ///   guarantees an ambiguous case always still gets a human's eyes on
    ///   it via the notification email CommentModerationProcessor always
    ///   sends regardless of score.
    /// </summary>
    public sealed class CloudflareWorkersAiScorer : IModerationScorer
    {
        private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            // The model sometimes returns the score as a quoted string
            // ({"score": "1", ...}) instead of a bare number ({"score": 1,
            // ...}) despite the prompt asking for an integer -- confirmed
            // in production 2026-09-10 with an otherwise well-formed,
            // non-truncated response that still failed to parse and fell
            // through to the fail-safe score of 0 purely because of the
            // quotes. AllowReadingFromString accepts both forms; it doesn't
            // change how a genuinely numeric value is read.
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private readonly HttpClient _httpClient;
        private readonly CloudflareWorkersAiSettings _cloudflareSettings;
        private readonly ModerationSettings _moderationSettings;

        public CloudflareWorkersAiScorer(
            HttpClient httpClient,
            CloudflareWorkersAiSettings cloudflareSettings,
            ModerationSettings moderationSettings)
        {
            _httpClient = httpClient;
            _cloudflareSettings = cloudflareSettings;
            _moderationSettings = moderationSettings;
        }

        public async Task<ModerationScore> ScoreAsync(string commentText)
        {
            var requestUrl = $"https://api.cloudflare.com/client/v4/accounts/{_cloudflareSettings.AccountId}/ai/run/{_moderationSettings.ModerationModelId}";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(new
                {
                    messages = new[]
                    {
                        new { role = "system", content = _moderationSettings.ModerationSystemPrompt },
                        new { role = "user", content = commentText }
                    },
                    // Belt and suspenders against a real production
                    // incident (2026-09-10): without an explicit cap,
                    // Workers AI's own default for this model cut
                    // generation off well before the closing brace of even
                    // the tiny {"score": N, "reason": "..."} shape the
                    // prompt asks for -- a raw response of exactly
                    // {"score": 3, "reason": "Low" with no closing quote/
                    // brace, which ParseModelResponseText correctly failed
                    // safe on, but at the cost of a real score (3) being
                    // silently downgraded to the fail-safe 0. This cap and
                    // ModerationSettings.ModerationSystemPrompt's own
                    // explicit word/token-budget instructions are two
                    // separate defenses against the same failure mode: the
                    // prompt keeps the model from trying to write a long
                    // response in the first place, this is the hard ceiling
                    // in case it ignores that anyway.
                    max_tokens = 200
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cloudflareSettings.ApiToken);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex) when (ex is not ModerationScoringException)
            {
                throw new ModerationScoringException("Failed to reach Cloudflare Workers AI.", ex);
            }

            using (response)
            {
                var rawBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new ModerationScoringException(
                        $"Cloudflare Workers AI returned {(int)response.StatusCode} {response.ReasonPhrase}: {rawBody}");
                }

                CloudflareAiEnvelope? envelope;
                try
                {
                    envelope = JsonSerializer.Deserialize<CloudflareAiEnvelope>(rawBody, CaseInsensitiveJsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new ModerationScoringException(
                        $"Could not parse Cloudflare Workers AI's response envelope: {rawBody}", ex);
                }

                if (envelope is null || !envelope.Success || envelope.Result?.Response is null)
                {
                    var errorDetail = envelope?.Errors is { Count: > 0 }
                        ? string.Join("; ", envelope.Errors.Select(e => e.Message))
                        : rawBody;
                    throw new ModerationScoringException($"Cloudflare Workers AI reported failure: {errorDetail}");
                }

                return ParseModelResponseText(envelope.Result.Response);
            }
        }

        /// <summary>
        /// Parses the model's raw generated text into a ModerationScore.
        /// Public specifically so this parsing/fail-safe logic (the actual
        /// interesting behavior) is directly unit-testable without any real
        /// HTTP call -- mirrors ApiSecurity's methods being public+testable
        /// in the sibling API project.
        /// </summary>
        public static ModerationScore ParseModelResponseText(string rawText)
        {
            var candidate = StripMarkdownCodeFence(rawText);

            try
            {
                var parsed = JsonSerializer.Deserialize<ModelScoreOutput>(candidate, CaseInsensitiveJsonOptions);
                if (parsed is not null && parsed.Score is >= 0 and <= 5 && !string.IsNullOrWhiteSpace(parsed.Reason))
                {
                    return new ModerationScore(parsed.Score, parsed.Reason!);
                }
            }
            catch (JsonException)
            {
                // Falls through to the fail-safe result below.
            }

            return new ModerationScore(
                0,
                $"Could not parse the moderation model's response as the expected JSON shape -- flagged for manual review. Raw response: {Truncate(rawText, 200)}");
        }

        // Some instruct models wrap JSON output in a markdown code fence
        // (```json ... ``` or plain ``` ... ```) even when explicitly told
        // not to -- stripped defensively before parsing rather than
        // rejecting the response outright.
        private static string StripMarkdownCodeFence(string text)
        {
            var trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0)
            {
                return trimmed;
            }

            var withoutOpeningFence = trimmed[(firstNewline + 1)..];
            var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
            return (closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex] : withoutOpeningFence).Trim();
        }

        private static string Truncate(string text, int maxLength) =>
            text.Length <= maxLength ? text : text[..maxLength] + "...";

        // Cloudflare's Workers AI response envelope -- e.g.
        // {"result": {"response": "..."}, "success": true, "errors": [], "messages": []}.
        private sealed record CloudflareAiEnvelope(bool Success, CloudflareAiResult? Result, List<CloudflareAiError>? Errors);

        private sealed record CloudflareAiResult(string? Response);

        private sealed record CloudflareAiError(int Code, string Message);

        // The shape ModerationSettings.ModerationSystemPrompt asks the
        // model to respond with, inside CloudflareAiResult.Response.
        private sealed record ModelScoreOutput(int Score, string? Reason);
    }
}
