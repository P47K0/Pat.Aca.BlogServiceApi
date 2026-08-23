using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public ApiTests(WebApplicationFactory<Program> factory)
        {
            // Swap the real repository for a fake, in-memory one so tests never
            // depend on appsettings.json's (placeholder) Cosmos config or a real
            // Cosmos DB connection.
            var testFactory = factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArticleRepository>();
                    services.AddSingleton<IArticleRepository, FakeArticleRepository>();
                }));

            _client = testFactory.CreateClient();
        }

        [Fact]
        public async Task GET_healthz_returns_200()
        {
            var response = await _client.GetAsync("/healthz");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_returns_seeded_articles()
        {
            var response = await _client.GetAsync("/articles");

            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadFromJsonAsync<List<Article>>();

            Assert.NotEmpty(articles);
        }

        [Fact]
        public async Task GET_articles_slug_returns_200_for_valid_slug()
        {
            var response = await _client.GetAsync("/articles/first-article");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_slug_returns_404_for_invalid_slug()
        {
            var response = await _client.GetAsync("/articles/nonexistent-slug");

            Assert.Equal(404, (int)response.StatusCode);
        }
    }

    public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags);
}
