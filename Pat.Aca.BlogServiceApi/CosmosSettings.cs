namespace Pat.Aca.BlogServiceApi
{
    public sealed class CosmosSettings
    {
        public string EndpointUri { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;

        // Set for key-based auth (e.g. the local Cosmos DB Emulator). Left unset,
        // CosmosArticleRepository authenticates via DefaultAzureCredential instead.
        public string? PrimaryKey { get; set; }
    }
}
