namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Credentials + addressing for the review-alert email, sent via Azure
    /// Communication Services (reusing the user's existing ACS resource
    /// from their other project, YoutubeChannelGuard -- no new resource).
    /// Kept separate from ModerationSettings, same "one settings class per
    /// external dependency" convention as CloudflareWorkersAiSettings.
    /// ConnectionString is the sensitive value here.
    /// </summary>
    public sealed class AcsEmailSettings
    {
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// The verified "from" address on the ACS-linked domain -- not a
        /// secret, but specific to how that resource is configured.
        /// </summary>
        public string SenderAddress { get; set; } = string.Empty;

        /// <summary>
        /// Where the review alert is sent -- the user's own address, kept
        /// as config rather than hardcoded, same reasoning as AzureAd:
        /// TenantId/ClientId already being config in the sibling API
        /// project rather than literals.
        /// </summary>
        public string RecipientEmail { get; set; } = string.Empty;
    }
}
