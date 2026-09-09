using System.Net;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace Pat.Aca.BlogServiceApi
{
    public sealed class CosmosCommentRepository : ICommentRepository
    {
        // Fixed, not read from CosmosSettings.Container (that property names
        // the Articles container specifically) — the Comments container's
        // name is a platform constant tied directly to the one declared in
        // infra/cosmos-db.bicep, not something a deployment should be able
        // to repoint via config.
        private const string CommentsContainerId = "Comments";

        private readonly Container _container;

        /// <summary>
        /// Builds its own CosmosClient rather than sharing
        /// CosmosArticleRepository's — a small, deliberate duplication of
        /// that class's connection logic (emulator cert bypass, Gateway
        /// mode, DefaultAzureCredential) rather than a bigger shared-client
        /// refactor, to keep this feature's changes scoped and avoid
        /// touching that already-shipped, production-critical class as a
        /// side effect of an unrelated feature. Worth extracting into a
        /// shared factory if a third repository ever needs this same logic.
        /// Reuses the same CosmosSettings (EndpointUri/Database/PrimaryKey)
        /// already registered for Articles — Comments lives in the same
        /// database, just a different container.
        /// </summary>
        public CosmosCommentRepository(CosmosSettings settings)
        {
            var clientOptions = new CosmosClientOptions();
            CosmosClient cosmosClient;

            if (!string.IsNullOrEmpty(settings.PrimaryKey))
            {
                var isLocalEmulator = Uri.TryCreate(settings.EndpointUri, UriKind.Absolute, out var endpoint)
                    && endpoint.IsLoopback;

                if (isLocalEmulator)
                {
                    clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    });
                    clientOptions.ConnectionMode = ConnectionMode.Gateway;
                }

                cosmosClient = new CosmosClient(settings.EndpointUri, settings.PrimaryKey, clientOptions);
            }
            else
            {
                // Direct mode needs a wide outbound TCP port range that
                // restricted container networking (ACA included) doesn't
                // reliably support — same reasoning as CosmosArticleRepository.
                clientOptions.ConnectionMode = ConnectionMode.Gateway;

                cosmosClient = new CosmosClient(
                    accountEndpoint: settings.EndpointUri,
                    tokenCredential: new DefaultAzureCredential(),
                    clientOptions: clientOptions);
            }

            _container = cosmosClient.GetDatabase(settings.Database).GetContainer(CommentsContainerId);
        }

        public async Task<Comment> CreateCommentAsync(string articleSlug, CommentWriteRequest request)
        {
            var document = new CommentDocument
            {
                Id = Guid.NewGuid().ToString(),
                ArticleSlug = articleSlug,
                AuthorName = request.AuthorName,
                Text = request.Text,
                CreatedAt = DateTime.UtcNow,
                Status = CommentStatus.Queued,
                Email = request.Email
            };

            ItemResponse<CommentDocument> response = await _container.CreateItemAsync(document, new PartitionKey(articleSlug));
            return ToComment(response.Resource);
        }

        public Task<List<Comment>> GetPublishedCommentsAsync(string articleSlug)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.articleSlug = @articleSlug AND c.status = @status ORDER BY c.createdAt ASC")
                .WithParameter("@articleSlug", articleSlug)
                .WithParameter("@status", CommentStatus.Published);

            return RunQueryAsync(query);
        }

        public Task<List<Comment>> GetAllCommentsAsync(string articleSlug)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.articleSlug = @articleSlug ORDER BY c.createdAt DESC")
                .WithParameter("@articleSlug", articleSlug);

            return RunQueryAsync(query);
        }

        private async Task<List<Comment>> RunQueryAsync(QueryDefinition query)
        {
            using FeedIterator<CommentDocument> iterator = _container.GetItemQueryIterator<CommentDocument>(query);
            var comments = new List<Comment>();

            while (iterator.HasMoreResults)
            {
                FeedResponse<CommentDocument> response = await iterator.ReadNextAsync();
                comments.AddRange(response.Resource.Select(ToComment));
            }

            return comments;
        }

        public async Task<Comment?> UpdateCommentStatusAsync(string articleSlug, string commentId, string status)
        {
            try
            {
                ItemResponse<CommentDocument> response = await _container.PatchItemAsync<CommentDocument>(
                    commentId,
                    new PartitionKey(articleSlug),
                    new[] { PatchOperation.Set("/status", status) });

                return ToComment(response.Resource);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> DeleteCommentAsync(string articleSlug, string commentId)
        {
            try
            {
                await _container.DeleteItemAsync<CommentDocument>(commentId, new PartitionKey(articleSlug));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        private static Comment ToComment(CommentDocument document) => new(
            document.Id,
            document.ArticleSlug,
            document.AuthorName,
            document.Text,
            document.CreatedAt,
            document.Status,
            document.LlmScore,
            document.Email);
    }
}
