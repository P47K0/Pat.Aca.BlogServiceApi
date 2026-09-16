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
    /// message. The user message is the comment's raw text alone when no
    /// article summary is available, or the summary followed by the
    /// comment when it is -- see IArticleContextProvider's own doc comment
    /// for why the model needs that to judge "on-topic" at all.
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
    ///   JSON, missing fields, an out-of-range score, or -- after fixes for
    ///   two real production incidents -- a quoted score or a stray extra
    ///   character before/after the object, both tolerated now rather than
    ///   failing) -- ParseModelResponseText fails safe to ModerationScore(0,
    ///   ...) instead of throwing. A formatting quirk in the model's output
    ///   isn't an infra problem retrying would fix, and defaulting to the
    ///   lowest score (rather than silently dropping the comment, or worse,
    ///   guessing it's fine) guarantees an ambiguous case always still gets
    ///   a human's eyes on it via the notification email
    ///   CommentModerationProcessor always sends regardless of score.
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

        public async Task<ModerationScore> ScoreAsync(string commentText, string? articleSummary)
        {
            var requestUrl = $"https://api.cloudflare.com/client/v4/accounts/{_cloudflareSettings.AccountId}/ai/run/{_moderationSettings.ModerationModelId}";

            // Loud, explicit labels matching ModerationSettings.
            // ModerationSystemPrompt's own ARTICLE_SUMMARY/COMMENT_TO_SCORE
            // wording -- a real production incident (2026-09-11) showed a
            // softer "Article summary: ...\n\nReader comment: ..." framing
            // wasn't enough for this small model: it ended up critiquing
            // the article's own quality instead of judging whether the
            // comment was safe to publish, the exact opposite of the
            // article-context feature's purpose. Falls back to the comment
            // alone (no labels at all) when the lookup failed or the
            // article has no summary -- see IArticleContextProvider's own
            // doc comment on why this is best-effort, never a hard
            // dependency of scoring itself.
            var userMessageContent = string.IsNullOrWhiteSpace(articleSummary)
                ? commentText
                : $"ARTICLE_SUMMARY: {articleSummary}\n\nCOMMENT_TO_SCORE: {commentText}";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(new
                {
                    messages = new[]
                    {
                        new { role = "system", content = _moderationSettings.ModerationSystemPrompt },
                        new { role = "user", content = userMessageContent }
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
            var candidate = ExtractBalancedJsonObject(StripMarkdownCodeFence(rawText));

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

        // Confirmed in production 2026-09-16: this tiny model sometimes
        // appends a stray extra closing brace after an otherwise
        // well-formed {"score": ..., "reason": "..."} object (e.g.
        // {"score": "4", "reason": "..."}} -- one } too many). Deserializing
        // straight from JsonSerializer rejects that as trailing data even
        // though the object itself parses fine, which used to silently
        // downgrade a real score to the fail-safe 0 purely because of the
        // extra character -- same failure shape as the truncated-response
        // incident this file's max_tokens comment describes, just an extra
        // character instead of a missing one. Scans from the first '{' and
        // tracks brace depth (skipping over string-literal contents, so a
        // reason that happens to mention "{" or "}" doesn't miscount) until
        // depth returns to zero, then discards everything after that point.
        // An object that never balances (e.g. genuinely truncated mid-
        // response) is returned from the first brace onward unchanged --
        // JsonSerializer.Deserialize still throws on that, same fail-safe
        // path as before this helper existed.
        private static string ExtractBalancedJsonObject(string text)
        {
            var start = text.IndexOf('{');
            if (start < 0)
            {
                return text;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        if (depth == 0)
                        {
                            return text[start..(i + 1)];
                        }
                        break;
                }
            }

            return text[start..];
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
