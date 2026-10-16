using System.Text.Json.Serialization;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// One changed Articles document as ArticleDetailSyncFunction's Change
    /// Feed trigger delivers it: the full current record, content included.
    /// Unlike ArticleChangeFeedDocument (id only, the count/list syncs
    /// re-query Cosmos), the detail sync pushes this document as-is, so it
    /// costs no extra Cosmos query. System.Text.Json attributes, since the
    /// isolated-worker trigger deserializes with System.Text.Json (see
    /// CommentChangeFeedDocument).
    ///
    /// No id: api-proxy and the API both return 0 for it (the Cosmos `id` is
    /// a GUID string on API-created articles, see #68). Dates stay the
    /// strings Cosmos stores (ISO 8601 UTC), forwarded as-is like the list
    /// push does.
    /// </summary>
    public sealed class ArticleDetailChangeFeedDocument
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public string PublishedAt { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("viewCount")]
        public int ViewCount { get; set; }

        [JsonPropertyName("linkedinVideoEmbedUrl")]
        public string? LinkedinVideoEmbedUrl { get; set; }

        [JsonPropertyName("seriesName")]
        public string? SeriesName { get; set; }

        [JsonPropertyName("seriesOrder")]
        public int? SeriesOrder { get; set; }

        [JsonPropertyName("relatedSlugs")]
        public List<string>? RelatedSlugs { get; set; }

        [JsonPropertyName("unlisted")]
        public bool Unlisted { get; set; }

        [JsonPropertyName("coverImageUrl")]
        public string? CoverImageUrl { get; set; }

        [JsonPropertyName("seoDescription")]
        public string? SeoDescription { get; set; }

        [JsonPropertyName("seoKeywords")]
        public List<string>? SeoKeywords { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }
}
