using System.Text.Json.Serialization;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The shape ArticleCountSyncFunction's Cosmos DB Change Feed trigger
    /// deserializes each changed Articles document into. Only Id is
    /// actually used (just to log which document triggered a sync) --
    /// unlike CommentChangeFeedDocument, this trigger doesn't need to
    /// inspect any other field: it recomputes the whole-container count via
    /// ArticleCountRepository on every delivery rather than reasoning about
    /// what specifically changed, so no other property needs to round-trip
    /// through this type. Same System.Text.Json attribute style as
    /// CommentChangeFeedDocument, for the same reason (the isolated-worker
    /// Cosmos DB trigger extension always deserializes via System.Text.Json,
    /// regardless of what the rest of the app uses).
    /// </summary>
    public sealed class ArticleChangeFeedDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}
