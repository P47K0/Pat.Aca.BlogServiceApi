using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Pat.Aca.BlogServiceApi
{
    using Azure.Core;
    using Azure.Identity;
    using Microsoft.Azure.Cosmos;

    public sealed class CosmosArticleRepository : IArticleRepository
    {
        private const string SlugPartitionKeyPath = "/slug";

        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;
        private readonly string _databaseId;
        private readonly string _containerId;

        // True only when this instance is talking to a local, key-authenticated,
        // loopback endpoint — i.e. the Cosmos DB Emulator. Gates behavior (cert
        // bypass, code-first database/container creation) that would be unsafe
        // to run against a real Azure Cosmos account.
        public bool IsLocalEmulator { get; }

        public CosmosArticleRepository(CosmosSettings settings)
        {
            _databaseId = settings.Database;
            _containerId = settings.Container;

            // NOTE: do not set a blanket CosmosSerializationOptions.PropertyNamingPolicy
            // here. Article.Id is confirmed stored as "Id" (PascalCase) — it's the
            // user's own custom field, distinct from Cosmos's mandatory system "id"
            // (which every document also has, separately, auto-generated). A blanket
            // CamelCase policy would make Id try to bind from that system "id" GUID
            // instead, breaking every read. See IncrementViewCountAsync for how the
            // view-count write targets the real system id without touching this.
            var clientOptions = new CosmosClientOptions();

            if (!string.IsNullOrEmpty(settings.PrimaryKey))
            {
                // Key-based auth: used for the local Cosmos DB Emulator (and, in
                // principle, any Cosmos account authenticated by key instead of
                // Azure AD/Managed Identity).
                IsLocalEmulator = Uri.TryCreate(settings.EndpointUri, UriKind.Absolute, out var endpoint)
                    && endpoint.IsLoopback;

                if (IsLocalEmulator)
                {
                    // The emulator serves a self-signed certificate. Only bypass
                    // validation for loopback endpoints — never for a real Cosmos
                    // account reached over the network.
                    clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    });
                    clientOptions.ConnectionMode = ConnectionMode.Gateway;
                }

                _cosmosClient = new CosmosClient(settings.EndpointUri, settings.PrimaryKey, clientOptions);
            }
            else
            {
                // Direct mode (the SDK default) needs a wide outbound TCP port
                // range straight to the Cosmos backend nodes, which restricted
                // container networking (Azure Container Apps included) doesn't
                // reliably support — Gateway mode routes everything over HTTPS
                // instead, same as the emulator branch above.
                clientOptions.ConnectionMode = ConnectionMode.Gateway;

                TokenCredential credential = new DefaultAzureCredential();

                _cosmosClient = new CosmosClient(
                    accountEndpoint: settings.EndpointUri,
                    tokenCredential: credential,
                    clientOptions: clientOptions);
            }

            _container = _cosmosClient.GetDatabase(_databaseId).GetContainer(_containerId);
        }

        /// <summary>
        /// Code-first provisioning for local development: creates the database and
        /// container if they don't already exist, partitioned by <c>/slug</c> per
        /// the BRD. Idempotent — safe to call on every startup. Callers should only
        /// invoke this when <see cref="IsLocalEmulator"/> is true.
        /// </summary>
        public async Task EnsureContainerExistsAsync()
        {
            DatabaseResponse databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_databaseId);
            await databaseResponse.Database.CreateContainerIfNotExistsAsync(_containerId, SlugPartitionKeyPath);
        }

        public async Task<Article?> GetArticleBySlugAsync(string slug)
        {
            // BRD: a scheduled (future publishedAt) article 404s on direct lookup
            // too, same as it's excluded from GetArticlesAsync.
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.slug = @slug AND c.publishedAt <= @now")
                .WithParameter("@slug", slug)
                .WithParameter("@now", DateTime.UtcNow);

            using FeedIterator<Article> iterator = _container.GetItemQueryIterator<Article>(query);

            while (iterator.HasMoreResults)
            {
                FeedResponse<Article> response = await iterator.ReadNextAsync();
                Article? article = response.Resource.FirstOrDefault();
                if (article is not null)
                {
                    return article;
                }
            }

            return null;
        }

        public async Task<List<Article>> GetArticlesAsync()
        {
            // BRD: newest-first, and a future publishedAt means "scheduled" —
            // excluded from public results until that time passes.
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.publishedAt <= @now ORDER BY c.publishedAt DESC")
                .WithParameter("@now", DateTime.UtcNow);

            using FeedIterator<Article> iterator = _container.GetItemQueryIterator<Article>(query);
            List<Article> articles = new();

            while (iterator.HasMoreResults)
            {
                FeedResponse<Article> response = await iterator.ReadNextAsync();
                articles.AddRange(response.Resource);
            }

            return articles;
        }

        public async Task<Article?> IncrementViewCountAsync(string slug)
        {
            // Cosmos's own system "id" — mandatory on every document, always a
            // lowercase string — is the one property name Cosmos itself
            // guarantees, unlike Article.Id (confirmed stored as "Id": the
            // user's own custom field, a completely different value) or any of
            // Article's other properties (their real casing was never verified
            // by a case-sensitive query, only ever read via case-insensitive
            // POCO binding). Look it up narrowly instead of guessing.
            // Same future-publishedAt exclusion as GetArticleBySlugAsync.
            var idQuery = new QueryDefinition(
                "SELECT VALUE c.id FROM c WHERE c.slug = @slug AND c.publishedAt <= @now")
                .WithParameter("@slug", slug)
                .WithParameter("@now", DateTime.UtcNow);

            string? cosmosId = null;
            using (FeedIterator<string> idIterator = _container.GetItemQueryIterator<string>(idQuery))
            {
                while (idIterator.HasMoreResults && cosmosId is null)
                {
                    FeedResponse<string> idResponse = await idIterator.ReadNextAsync();
                    cosmosId = idResponse.Resource.FirstOrDefault();
                }
            }

            if (cosmosId is null)
            {
                return null;
            }

            // A targeted Patch, not a read-modify-write/Upsert of the whole
            // article: avoids needing to know or match the real casing of any
            // property but "/viewCount", which is new and freely named here.
            // Increment creates the field (starting from the given value) if
            // it doesn't exist yet — confirmed via Cosmos's Patch API docs —
            // so this is safe on an article that's never been viewed before.
            // Also atomic, unlike a read-modify-write: concurrent views can't
            // lose an update.
            ItemResponse<Article> response = await _container.PatchItemAsync<Article>(
                cosmosId,
                new PartitionKey(slug),
                new[] { PatchOperation.Increment("/viewCount", 1) });

            return response.Resource;
        }

        public async Task<List<ArticleSummary>> GetRecentArticlesAsync(int count = 5)
        {
            var query = new QueryDefinition(
                "SELECT TOP @count c.slug, c.title, c.summary, c.publishedAt FROM c ORDER BY c.publishedAt DESC")
                .WithParameter("@count", count);

            using FeedIterator<ArticleSummary> iterator = _container.GetItemQueryIterator<ArticleSummary>(query);
            List<ArticleSummary> articles = new();

            while (iterator.HasMoreResults)
            {
                FeedResponse<ArticleSummary> response = await iterator.ReadNextAsync();
                articles.AddRange(response.Resource);
            }

            return articles;
        }
    }

    public sealed class ArticleSummary
    {
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }
}
