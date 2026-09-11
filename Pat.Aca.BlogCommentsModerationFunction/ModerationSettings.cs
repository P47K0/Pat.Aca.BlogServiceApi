namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Tunables for the moderation pipeline, bound from Function App
    /// settings (local.settings.json locally) -- same "plain-POCO,
    /// configurable rather than hardcoded" convention as
    /// Pat.Aca.BlogServiceApi's CommentSettings/CosmosSettings.
    /// </summary>
    public sealed class ModerationSettings
    {
        /// <summary>
        /// Which Cloudflare Workers AI model scores each comment. Kept
        /// configurable specifically so it can be swapped without a
        /// redeploy -- see MODERATION_MODEL_ID.
        /// </summary>
        public string ModerationModelId { get; set; } = "@cf/meta/llama-3.2-1b-instruct";

        /// <summary>
        /// Max comments scored per day -- protects against a single source
        /// (or a bug) driving up Workers AI usage unboundedly. Not hardcoded
        /// so it can be raised once real usage/cost is observed -- see
        /// DAILY_MODERATION_QUOTA.
        /// </summary>
        public int DailyModerationQuota { get; set; } = 25;

        /// <summary>
        /// A comment scoring at or above this threshold is auto-published
        /// instead of landing at Unpublished for manual review. Default is
        /// deliberately set above the max possible score (5), so nothing
        /// auto-publishes until this is explicitly lowered -- expressing
        /// "the human is always in the middle" with zero extra logic, not a
        /// separate on/off flag. Lower it later (e.g. to 4) once the LLM's
        /// judgement is trusted -- see AUTO_PUBLISH_MIN_SCORE.
        /// </summary>
        public int AutoPublishMinScore { get; set; } = 6;

        /// <summary>
        /// The system-message instructions sent to the LLM alongside each
        /// comment's raw text (as a separate user message -- see
        /// IModerationScorer's real implementation) to produce a
        /// ModerationScore. Kept configurable and separate from
        /// ModerationModelId deliberately -- wording will likely need
        /// several rounds of tuning against real comments/spam before it's
        /// trusted enough to lower AutoPublishMinScore, and that's a much
        /// faster iteration loop as a Function App setting (MODERATION_
        /// SYSTEM_PROMPT) than as a code change requiring a redeploy. The
        /// default asks for strict JSON specifically so the scorer's
        /// response parsing doesn't have to cope with free-form prose.
        /// </summary>
        public string ModerationSystemPrompt { get; set; } =
            "You are a content moderator for a personal blog's comment section. " +
            "You will be shown two labeled pieces of text: ARTICLE_SUMMARY and COMMENT_TO_SCORE. " +
            "You are scoring ONLY the COMMENT_TO_SCORE. ARTICLE_SUMMARY is background context, " +
            "given solely so you can judge whether the comment is on-topic -- never evaluate, " +
            "critique, or rate the article itself, and never let the article's own quality affect " +
            "the comment's score. If ARTICLE_SUMMARY is missing, judge the comment on its own " +
            "merits without penalizing it for being unable to confirm relevance to the article. " +
            "Score how safe the comment is to publish on a scale of 0 to 5: " +
            "0 means definitely do not publish, 5 means definitely fine to publish. " +
            "Score low for offensive, hateful, or sexual content; commercial spam or advertising; " +
            "and low-quality garbage (gibberish, irrelevant text, or obvious bot output). " +
            "Score high for genuine, on-topic reader engagement, even if critical or negative in tone. " +
            "Respond with ONLY a single JSON object, no other text, in exactly this shape: " +
            "{\"score\": <integer 0-5>, \"reason\": \"<one short sentence, 12 words or fewer, explaining the score>\"}. " +
            "Keep the whole response well under 200 tokens.";
    }
}
