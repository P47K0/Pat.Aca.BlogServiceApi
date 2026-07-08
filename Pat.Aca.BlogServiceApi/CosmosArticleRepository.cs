using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;

namespace Pat.Aca.BlogServiceApi
{
    public class CosmosArticleRepository : IArticleRepository
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Database _database;
        private readonly Container _container;

        public CosmosArticleRepository(CosmosClientOptions options)
        {
            _cosmosClient = new CosmosClient(options.EndpointUrl, options.AuthKey);
            _database = _cosmosClient.GetDatabase("ArticlesDB");
            _container = _database.GetContainer("Articles");
        }

        public async Task<List<Article>> GetArticlesAsync()
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
    }
}
