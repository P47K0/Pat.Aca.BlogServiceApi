namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Credentials for calling Cloudflare Workers AI directly over HTTP
    /// (api.cloudflare.com/client/v4/accounts/{AccountId}/ai/run/{model}) --
    /// kept separate from ModerationSettings, which is behavior tuning
    /// (model choice, prompt, quota, threshold), not credentials. Mirrors
    /// CosmosSettings' own plain-POCO, bound-from-config convention;
    /// ApiToken is the one sensitive value here, same as CosmosSettings.
    /// PrimaryKey being mixed into an otherwise-non-secret settings class in
    /// the sibling API project -- this codebase's existing precedent for
    /// "one settings class per external dependency" already does this.
    /// </summary>
    public sealed class CloudflareWorkersAiSettings
    {
        public string AccountId { get; set; } = string.Empty;
        public string ApiToken { get; set; } = string.Empty;
    }
}
