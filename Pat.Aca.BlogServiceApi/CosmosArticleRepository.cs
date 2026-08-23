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
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;

        public CosmosArticleRepository(CosmosSettings settings)
        {
            TokenCredential credential = new DefaultAzureCredential();

            _cosmosClient = new CosmosClient(
                accountEndpoint: settings.EndpointUri,
                tokenCredential: credential,
                clientOptions: new CosmosClientOptions());

            _container = _cosmosClient
                .GetDatabase(settings.Database)
                .GetContainer(settings.Container);
        }

        public async Task<Article?> GetArticleBySlugAsync(string slug)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.slug = @slug")
                .WithParameter("@slug", slug);

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
            var query = new QueryDefinition("SELECT * FROM c");

            using FeedIterator<Article> iterator = _container.GetItemQueryIterator<Article>(query);
            List<Article> articles = new();

            while (iterator.HasMoreResults)
            {
                FeedResponse<Article> response = await iterator.ReadNextAsync();
                articles.AddRange(response.Resource);
            }

            return articles;
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
