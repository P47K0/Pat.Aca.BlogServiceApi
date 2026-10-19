using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IPublishedCommentsRepository: the API's GetPublishedCommentsAsync
    /// filter (status published), across all articles at once. Sorted here
    /// rather than with a cross-partition ORDER BY. Not unit tested, like
    /// this project's other Cosmos-calling classes.
    /// </summary>
    public sealed class CosmosPublishedCommentsRepository(Container commentsContainer) : IPublishedCommentsRepository
    {
        public async Task<Dictionary<string, List<PublishedComment>>> GetPublishedCommentsBySlugAsync(CancellationToken cancellationToken = default)
        {
            var query = new QueryDefinition(
                "SELECT c.id, c.articleSlug, c.authorName, c.text, c.createdAt FROM c WHERE c.status = @status")
                .WithParameter("@status", ModerationCommentStatus.Published);

            using FeedIterator<CommentDocument> iterator = commentsContainer.GetItemQueryIterator<CommentDocument>(query);
            var documents = new List<CommentDocument>();
            while (iterator.HasMoreResults)
            {
                FeedResponse<CommentDocument> response = await iterator.ReadNextAsync(cancellationToken);
                documents.AddRange(response.Resource);
            }

            return documents
                .GroupBy(document => document.ArticleSlug)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(document => document.CreatedAt)
                        .Select(document => new PublishedComment(document.Id, document.AuthorName, document.Text, document.CreatedAt))
                        .ToList());
        }

        private sealed class CommentDocument
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;

            [JsonProperty("articleSlug")]
            public string ArticleSlug { get; set; } = string.Empty;

            [JsonProperty("authorName")]
            public string AuthorName { get; set; } = string.Empty;

            [JsonProperty("text")]
            public string Text { get; set; } = string.Empty;

            [JsonProperty("createdAt")]
            public DateTime CreatedAt { get; set; }
        }
    }
}
