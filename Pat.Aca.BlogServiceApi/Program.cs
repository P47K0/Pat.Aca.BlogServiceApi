using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Pat.Aca.BlogServiceApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

var cosmosDbSettings = builder.Configuration.GetSection("CosmosDb").Get<CosmosSettings>();
var cosmosConfigured = cosmosDbSettings is not null
    && Uri.TryCreate(cosmosDbSettings.EndpointUri, UriKind.Absolute, out _)
    && !string.IsNullOrWhiteSpace(cosmosDbSettings.Database)
    && !string.IsNullOrWhiteSpace(cosmosDbSettings.Container);

if (cosmosConfigured)
{
    builder.Services.AddSingleton(cosmosDbSettings!);
    builder.Services.AddSingleton<IArticleRepository, CosmosArticleRepository>();
}
else
{
    builder.Services.AddSingleton<IArticleRepository, InMemoryArticleRepository>();
}

const string ArticlesRateLimiterPolicy = "articles";

// Throttle the public read endpoints the Cloudflare Worker calls. Partitioned by
// client IP for now; revisit once API-key auth is wired up so the Worker's own
// key (rather than its shared egress IP) can be used as the partition key.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(ArticlesRateLimiterPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (cosmosConfigured)
{
    app.Logger.LogInformation(
        "CosmosDb configured (endpoint {EndpointUri}) — using CosmosArticleRepository.",
        cosmosDbSettings!.EndpointUri);
}
else
{
    app.Logger.LogWarning(
        "CosmosDb config is incomplete (need a valid absolute EndpointUri, Database, and Container) — falling back to InMemoryArticleRepository.");
}

// Code-first provisioning, local emulator only: never auto-creates a database or
// container against a real Azure Cosmos account. Failures here are logged, not
// fatal — e.g. the emulator container may simply not be up yet — so a single
// dev-convenience step never takes down the whole app at startup.
if (app.Services.GetRequiredService<IArticleRepository>() is CosmosArticleRepository { IsLocalEmulator: true } cosmosRepository)
{
    app.Logger.LogInformation("Local Cosmos DB Emulator detected — ensuring database/container exist.");
    try
    {
        await cosmosRepository.EnsureContainerExistsAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "Could not reach the Cosmos DB Emulator to provision the database/container. " +
            "Is it running? Article endpoints will fail until it's reachable.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI reads the OpenAPI document MapOpenApi() already serves at
    // /openapi/v1.json — this only adds the browsable UI, not a second
    // document generator. Dev-only: not something to expose publicly.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Blog Service API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var scopeRequiredByApi = app.Configuration["AzureAd:Scopes"] ?? "";

app.MapGet("/healthz", () => "Healthy");

app.MapGet("/articles", async (IArticleRepository articleRepository) =>
{
    var articles = await articleRepository.GetArticlesAsync();
    return Results.Json(articles);
}).RequireRateLimiting(ArticlesRateLimiterPolicy);

app.MapGet("/articles/{slug}", async Task<IResult> (string slug, IArticleRepository articleRepository) =>
{
    if (string.IsNullOrEmpty(slug))
    {
        return Results.BadRequest("Invalid slug");
    }

    var article = await articleRepository.GetArticleBySlugAsync(slug);
    return article is null ? Results.NotFound("Article not found") : Results.Json(article);
}).RequireRateLimiting(ArticlesRateLimiterPolicy);

app.Run();

public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags);
public interface IArticleRepository
{
    Task<List<Article>> GetArticlesAsync();
    Task<Article?> GetArticleBySlugAsync(string slug);
}

public sealed class CosmosSettings
{
    public string EndpointUri { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;

    // Set for key-based auth (e.g. the local Cosmos DB Emulator). Left unset,
    // CosmosArticleRepository authenticates via DefaultAzureCredential instead.
    public string? PrimaryKey { get; set; }
}

public class InMemoryArticleRepository : IArticleRepository
{
    private static readonly List<Article> SeedArticles = new()
    {
        new Article(1, "first-article", "First Article", "Summary of first article", "Content of first article", DateTime.UtcNow, new List<string> { "tag1" }),
        new Article(2, "second-article", "Second Article", "Summary of second article", "Content of second article", DateTime.UtcNow, new List<string> { "tag2" })
    };

    public Task<List<Article>> GetArticlesAsync() => Task.FromResult(SeedArticles);

    public Task<Article?> GetArticleBySlugAsync(string slug) =>
        Task.FromResult(SeedArticles.FirstOrDefault(a => a.Slug == slug));
}
// TODO:
// dotnet ef migrations add InitialCreate
// dotnet ef database update