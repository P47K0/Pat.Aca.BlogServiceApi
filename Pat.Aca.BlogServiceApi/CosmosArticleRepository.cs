using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Pat.Aca.BlogServiceApi
{
    public class CosmosArticleRepository : IArticleRepository
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Database _database;
        private readonly Container _container;

        public CosmosArticleRepository(string endpointUri)
        {
            TokenCredential credential = new DefaultAzureCredential();

            _cosmosClient = new CosmosClient(
                accountEndpoint: endpointUri,
                tokenCredential: credential,
                clientOptions: new CosmosClientOptions());

            _database = _cosmosClient.GetDatabase("ArticlesDB");
            _container = _database.GetContainer("Articles");
        }

        public async Task<List<Article>> GetAllAsync()
        {
            var query = "SELECT * FROM c";
            var feedIterator = _container.GetItemQueryIterator<Article>(query);

            List<Article> articles = new();
            while (feedIterator.HasMoreResults)
            {
                var response = await feedIterator.ReadNextAsync();
                articles.AddRange(response.Resource);
            }

            return articles;
        }

        public async Task<Article?> GetArticleBySlugAsync(string slug)
        {
            var query = $"SELECT * FROM c WHERE c.Slug = '{slug}'";
            var feedIterator = _container.GetItemQueryIterator<Article>(query);

            while (feedIterator.HasMoreResults)
            {
                var response = await feedIterator.ReadNextAsync();
                return response.Resource.FirstOrDefault();
            }

            return null;
        }

        public Task<List<Article>> GetArticlesAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<Article?> GetBySlugAsync(string slug)
        {
            var query = $"SELECT * FROM c WHERE c.Slug = '{slug}'";
            var feedIterator = _container.GetItemQueryIterator<Article>(query);

            while (feedIterator.HasMoreResults)
            {
                var response = await feedIterator.ReadNextAsync();
                return response.Resource.FirstOrDefault();
            }

            return null;
        }
    }
}
