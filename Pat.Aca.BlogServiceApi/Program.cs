using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Microsoft.OpenApi;
using Pat.Aca.BlogServiceApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Standardizes error bodies as RFC 7807 ProblemDetails JSON. Combined with
// UseStatusCodePages() below, this covers both explicit Results.Problem(...)
// calls and otherwise-empty error responses (unmatched routes, the bare 401
// from Results.Unauthorized(), the rate limiter's 429) with one consistent
// shape, instead of a mix of plain strings and ad hoc bodies.
builder.Services.AddProblemDetails();

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
const string ApiKeyHeaderName = ApiSecurity.ApiKeyHeaderName;

// Shared secret the Cloudflare Worker sends on the article endpoints' behalf.
// Not set via DefaultAzureCredential/Cosmos-style fallback on purpose — the BRD
// calls for a single static key, kept simple, separate from the Azure AD path
// reserved for a future admin/write API.
var apiKey = builder.Configuration["ApiKey"];

// Throttle the public read endpoints the Cloudflare Worker calls. Partition
// selection and options construction live in ApiSecurity (unit-tested there)
// — this just wires them in.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(ArticlesRateLimiterPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ApiSecurity.GetRateLimitPartitionKey(httpContext),
            factory: _ => ApiSecurity.CreateArticlesLimiterOptions(permitLimit: 60, window: TimeSpan.FromMinutes(1))));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new();
        // Just assigned above — the nullable analyzer doesn't trust that across a
        // property re-read, so this collapses it to one known-safe suppression.
        var components = document.Components!;
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyHeaderName,
            Description = "Shared-secret key sent by the Cloudflare Worker on behalf of the frontend."
        };

        // Only the article endpoints require the key — /healthz stays open.
        var articleOperations = document.Paths
            .Where(path => path.Key.StartsWith("/articles", StringComparison.Ordinal))
            .SelectMany(path => path.Value.Operations!.Values);

        foreach (var operation in articleOperations)
        {
            operation.Security ??= [];
            operation.Security.Add(new()
            {
                [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
            });
        }

        return Task.CompletedTask;
    });
});

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

if (string.IsNullOrEmpty(apiKey))
{
    app.Logger.LogWarning(
        "ApiKey is not configured — article endpoints will reject every request (fail closed) until it's set.");
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

// Must wrap everything downstream that can produce a bare error status code,
// so it belongs early in the pipeline, before auth/rate limiting/endpoints.
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var scopeRequiredByApi = app.Configuration["AzureAd:Scopes"] ?? "";

// The actual check lives in ApiSecurity (unit-tested there); this just
// supplies the configured key from the closure.
ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
    ApiSecurity.RequireApiKey(context, next, apiKey);

app.MapGet("/healthz", () => "Healthy");

app.MapGet("/articles", async (IArticleRepository articleRepository) =>
{
    var articles = await articleRepository.GetArticlesAsync();
    return Results.Json(articles);
})
    .RequireRateLimiting(ArticlesRateLimiterPolicy)
    .AddEndpointFilter(RequireApiKey);

app.MapGet("/articles/{slug}", async Task<IResult> (string slug, IArticleRepository articleRepository) =>
{
    if (string.IsNullOrEmpty(slug))
    {
        return Results.Problem("Invalid slug", statusCode: StatusCodes.Status400BadRequest);
    }

    var article = await articleRepository.GetArticleBySlugAsync(slug);
    return article is null
        ? Results.Problem("Article not found", statusCode: StatusCodes.Status404NotFound)
        : Results.Json(article);
})
    .RequireRateLimiting(ArticlesRateLimiterPolicy)
    .AddEndpointFilter(RequireApiKey);

app.Run();

// Article, IArticleRepository, CosmosSettings, and InMemoryArticleRepository
// now live in their own files (see Article.cs, IArticleRepository.cs,
// CosmosSettings.cs, InMemoryArticleRepository.cs).

// TODO:
// dotnet ef migrations add InitialCreate
// dotnet ef database update