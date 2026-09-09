using System.Text.Json.Serialization;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The shape CommentModerationFunction's Cosmos DB Change Feed trigger
    /// deserializes each changed document into. Uses System.Text.Json
    /// attributes, not Newtonsoft's [JsonProperty] like everything else
    /// Cosmos-facing in this project -- the isolated-worker Cosmos DB
    /// trigger extension deserializes trigger payloads via System.Text.Json
    /// regardless of what the rest of the app uses, so the property names
    /// still need to match the container's real camelCase field names, just
    /// via a different attribute.
    ///
    /// The Change Feed delivers every change in this container, including
    /// the daily quota counter document (CosmosModerationQuotaStore) and
    /// this Function's own status-patch writes back onto already-moderated
    /// comments -- both are naturally filtered out by
    /// CommentModerationFunction checking Status == ModerationCommentStatus.
    /// Queued, since neither the quota document nor an already-moderated
    /// comment ever has that value.
    /// </summary>
    public sealed class CommentChangeFeedDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("articleSlug")]
        public string ArticleSlug { get; set; } = "";

        [JsonPropertyName("authorName")]
        public string AuthorName { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
