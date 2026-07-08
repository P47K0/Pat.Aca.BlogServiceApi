using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pat.ACA.BlogServiceTests
{
    public class ArticlesApiTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ArticlesApiTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GET_healthz_returns_200()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/healthz");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_returns_seeded_articles()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/articles");

            response.EnsureSuccessStatusCode();
            var articles = await response.Content.ReadAsAsync<List<Article>>();

            Assert.NotEmpty(articles);
        }

        [Fact]
        public async Task GET_articles_slug_returns_200_for_valid_slug()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/articles/first-article");

            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task GET_articles_slug_returns_404_for_invalid_slug()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/articles/nonexistent-slug");

            Assert.Equal(404, (int)response.StatusCode);
        }
    }

    public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags);
}
