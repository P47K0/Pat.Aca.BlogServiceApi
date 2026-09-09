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
    }
}
