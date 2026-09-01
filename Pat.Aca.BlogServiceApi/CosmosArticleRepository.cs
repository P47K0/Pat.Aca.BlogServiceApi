using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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

        /// <summary>
        /// Looks up an article's real Cosmos system id by slug, with no
        /// publishedAt filter — unlike GetArticleBySlugAsync/
        /// IncrementViewCountAsync above, which deliberately hide future-dated
        /// articles from readers/view-counting, the write path needs to find an
        /// article regardless of publish state: an author must be able to see
        /// and edit their own scheduled drafts. Returns null if no document has
        /// this slug.
        /// </summary>
        private async Task<string?> FindCosmosIdBySlugAsync(string slug)
        {
            var query = new QueryDefinition("SELECT VALUE c.id FROM c WHERE c.slug = @slug")
                .WithParameter("@slug", slug);

            using FeedIterator<string> iterator = _container.GetItemQueryIterator<string>(query);
            while (iterator.HasMoreResults)
            {
                FeedResponse<string> response = await iterator.ReadNextAsync();
                var id = response.Resource.FirstOrDefault();
                if (id is not null)
                {
                    return id;
                }
            }

            return null;
        }

        public async Task<Article?> CreateArticleAsync(ArticleWriteRequest request)
        {
            // No publishedAt filter — a draft/future-dated slug still reserves
            // the name. This existence check + the CreateItemAsync below aren't
            // atomic together (a true concurrent double-POST of the same slug
            // could theoretically slip both through) — an accepted simplicity
            // trade-off at this blog's traffic/single-caller scale, same as the
            // read-modify-write view-count implementation was before it got
            // upgraded to Patch.
            if (await FindCosmosIdBySlugAsync(request.Slug) is not null)
            {
                return null;
            }

            var document = new ArticleDocument
            {
                Id = Guid.NewGuid().ToString(),
                Slug = request.Slug,
                Title = request.Title,
                Summary = request.Summary ?? "",
                Content = request.Content,
                PublishedAt = request.PublishedAt,
                Tags = request.Tags ?? new List<string>(),
                ViewCount = 0
            };

            ItemResponse<ArticleDocument> response = await _container.CreateItemAsync(document, new PartitionKey(document.Slug));

            return ToArticle(response.Resource);
        }

        public async Task<Article?> UpdateArticleAsync(string slug, ArticleWriteRequest request)
        {
            var cosmosId = await FindCosmosIdBySlugAsync(slug);
            if (cosmosId is null)
            {
                // Update-only, not upsert.
                return null;
            }

            // A targeted Patch, not a full ReplaceItemAsync of the whole
            // document — same reasoning as IncrementViewCountAsync above: this
            // never has to read or preserve viewCount (or guess any other
            // property's real casing on existing hand-authored documents),
            // since it only ever touches the specific paths named here.
            var patchOperations = new List<PatchOperation>
            {
                PatchOperation.Set("/title", request.Title),
                PatchOperation.Set("/summary", request.Summary ?? ""),
                PatchOperation.Set("/content", request.Content),
                PatchOperation.Set("/publishedAt", request.PublishedAt),
                PatchOperation.Set("/tags", request.Tags ?? new List<string>())
            };

            ItemResponse<ArticleDocument> response = await _container.PatchItemAsync<ArticleDocument>(
                cosmosId,
                new PartitionKey(slug),
                patchOperations);

            return ToArticle(response.Resource);
        }

        private static Article ToArticle(ArticleDocument document) =>
            // Id is always 0 here — dropped from the write path per the BRD.
            // Existing hand-authored articles keep their old (PascalCase-stored)
            // Id value untouched; it's simply never read or written by this
            // class's write methods.
            new(0, document.Slug, document.Title, document.Summary, document.Content, document.PublishedAt, document.Tags, document.ViewCount);

        /// <summary>
        /// The exact JSON shape written to/read from Cosmos by the write
        /// methods above. Deliberately separate from the public Article
        /// record: Cosmos's default serializer (Newtonsoft, no naming policy —
        /// see the comment on CosmosClientOptions in the constructor) would
        /// otherwise write PascalCase property names straight from Article's
        /// C# member names, which would silently break every future read —
        /// GetArticlesAsync/GetArticleBySlugAsync's WHERE clauses require
        /// "slug" and "publishedAt" specifically lowercase (confirmed via
        /// production data), and the view-count Patch above already targets
        /// "/viewCount" lowercase camelCase. This type pins every field this
        /// class writes to that same lowercase-camelCase convention
        /// explicitly. The legacy "Id" field is deliberately not included —
        /// dropped from the write path per the BRD.
        /// </summary>
        private sealed class ArticleDocument
        {
            [JsonProperty("id")]
            public string Id { get; set; } = "";

            [JsonProperty("slug")]
            public string Slug { get; set; } = "";

            [JsonProperty("title")]
            public string Title { get; set; } = "";

            [JsonProperty("summary")]
            public string Summary { get; set; } = "";

            [JsonProperty("content")]
            public string Content { get; set; } = "";

            [JsonProperty("publishedAt")]
            public DateTime PublishedAt { get; set; }

            [JsonProperty("tags")]
            public List<string> Tags { get; set; } = new();

            [JsonProperty("viewCount")]
            public int ViewCount { get; set; }
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
