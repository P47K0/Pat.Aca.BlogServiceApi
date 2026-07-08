using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Pat.Aca.BlogServiceApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

var scopeRequiredByApi = app.Configuration["AzureAd:Scopes"] ?? "";
var cosmosDbSettings = builder.Configuration.GetSection("CosmosDb").Get<CosmosDbSettings>();
if (cosmosDbSettings != null)
{
    var articleRepository = new CosmosArticleRepository(cosmosDbSettings.EndpointUri);
}
else
{
    var articleRepository = new InMemoryArticleRepository();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/healthz", () => "Healthy");
app.MapGet("/articles", async context =>
{
    var articles = await GetArticlesAsync();
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(articles);
});
app.MapGet("/articles/{slug}", async context =>
{
    var slug = context.Request.RouteValues["slug"] as string;
    if (string.IsNullOrEmpty(slug))
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Invalid slug");
        return;
    }

    Article? article = null;
    var cosmosDbSettings = builder.Configuration.GetSection("CosmosDb").Get<CosmosDbSettings>();
    if (cosmosDbSettings != null)
    {
        var articleRepository = new CosmosArticleRepository(cosmosDbSettings.EndpointUri);
        article = await articleRepository.GetArticleBySlugAsync(slug);
        if (article == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Article not found");
            return;
        }
    }
    else
    {
        article = await GetArticleBySlugAsync(slug);
        if (article == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Article not found");
            return;
        }
    }

    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(article);
});

app.Run();

async Task<List<Article>> GetArticlesAsync()
{
    // In-memory fake implementation
    return new List<Article>
    {
        new Article(1, "first-article", "First Article", "Summary of first article", "Content of first article", DateTime.Now, new List<string> { "tag1" }),
        new Article(2, "second-article", "Second Article", "Summary of second article", "Content of second article", DateTime.Now, new List<string> { "tag2" })
    };
}

async Task<Article?> GetArticleBySlugAsync(string slug)
{
    // In-memory fake implementation
    return await Task.FromResult(GetArticlesAsync().Result.FirstOrDefault(a => a.Slug == slug));
}

public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags);
public interface IArticleRepository
{
    Task<List<Article>> GetArticlesAsync();
    Task<Article?> GetArticleBySlugAsync(string slug);
}

public class CosmosDbSettings
{
    public string EndpointUri { get; set; }
    public string PrimaryKey { get; set; }
}

public class InMemoryArticleRepository : IArticleRepository
{
    public async Task<List<Article>> GetArticlesAsync()
    {
        return await Task.FromResult(GetArticlesAsync().Result);
    }

    public async Task<Article?> GetArticleBySlugAsync(string slug)
    {
        return await Task.FromResult(GetArticleBySlugAsync(slug).Result);
    }
}
// TODO:
// dotnet ef migrations add InitialCreate
// dotnet ef database update