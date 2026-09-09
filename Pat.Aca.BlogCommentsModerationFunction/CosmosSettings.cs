namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Cosmos connection info for this Function -- duplicated from the
    /// sibling API project's own CosmosSettings rather than shared (same
    /// no-project-reference reasoning as ModerationCommentStatus). Points
    /// at the same account/database the API uses; the Comments container
    /// name itself is a fixed constant in CosmosModerationQuotaStore, not
    /// a configurable property here, matching CosmosCommentRepository's
    /// own convention in the API project.
    /// </summary>
    public sealed class CosmosSettings
    {
        public string EndpointUri { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;

        /// <summary>Set for the local Cosmos DB Emulator; left unset in
        /// production, where DefaultAzureCredential is used instead.</summary>
        public string? PrimaryKey { get; set; }
    }
}
