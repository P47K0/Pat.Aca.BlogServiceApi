using Newtonsoft.Json;

namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// The exact JSON shape written to/read from the Comments Cosmos
    /// container. Kept separate from the public Comment record for the same
    /// reason ArticleDocument is kept separate from Article (see
    /// CosmosArticleRepository.ArticleDocument's own doc comment): Cosmos's
    /// default serializer (Newtonsoft, no naming policy) would otherwise
    /// write PascalCase property names straight from the C# member names,
    /// which would break the /articleSlug partition key (must match the
    /// bicep-declared path exactly) and any case-sensitive WHERE clause a
    /// future repository query relies on.
    ///
    /// Unlike Article's optional fields (LinkedinVideoEmbedUrl, SeriesName,
    /// etc.), which are written as an explicit JSON null when absent, Email
    /// is deliberately configured to be omitted from the document entirely
    /// when not provided (NullValueHandling.Ignore) rather than stored as
    /// null — a small data-minimization step specific to this one field,
    /// since it's the only piece of reader PII this container holds.
    /// LlmScore follows the ordinary explicit-null convention: it's a
    /// meaningful "not yet scored" state, not something to hide.
    ///
    /// This type currently has no repository to belong to yet (that's a
    /// later commit) — kept top-level and internal so it's ready to be
    /// consumed once CosmosCommentRepository exists, same shape ArticleDocument
    /// would have if it weren't already nested inside CosmosArticleRepository.
    /// </summary>
    internal sealed class CommentDocument
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("articleSlug")]
        public string ArticleSlug { get; set; } = "";

        [JsonProperty("authorName")]
        public string AuthorName { get; set; } = "";

        [JsonProperty("text")]
        public string Text { get; set; } = "";

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } = CommentStatus.Queued;

        [JsonProperty("llmScore")]
        public int? LlmScore { get; set; }

        [JsonProperty("email", NullValueHandling = NullValueHandling.Ignore)]
        public string? Email { get; set; }
    }
}
