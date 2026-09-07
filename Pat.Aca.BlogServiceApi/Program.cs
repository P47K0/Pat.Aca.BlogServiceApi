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

const string ArticlesWriteAuthorizationPolicy = "ArticlesWrite";
const string ArticlesWriteRoleName = "Articles.Write";

// The write endpoints require this app role on the token, not just "any
// valid token" — deliberately structured (per the BRD) so a narrower
// Articles.Delete role can be added later without redesigning this.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ArticlesWriteAuthorizationPolicy, policy => policy.RequireRole(ArticlesWriteRoleName));
});

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
const string ArticlesWriteRateLimiterPolicy = "articles-write";
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

    // A separate, tighter policy for the write endpoints — well below the
    // read path's 60/min, since there's exactly one legitimate caller
    // (Claude, via the client-credentials app) and no read-scale traffic to
    // accommodate. Partitioned by AAD identity, not API key — see
    // ApiSecurity.GetWriteRateLimitPartitionKey.
    options.AddPolicy(ArticlesWriteRateLimiterPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ApiSecurity.GetWriteRateLimitPartitionKey(httpContext),
            factory: _ => ApiSecurity.CreateArticlesLimiterOptions(permitLimit: 20, window: TimeSpan.FromMinutes(1))));
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

        components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Azure AD app-only token (client-credentials grant) carrying the Articles.Write app role — required for POST/PUT /articles."
        };

        // Only the article endpoints require the key — /healthz stays open.
        var articleOperations = document.Paths
            .Where(path => path.Key.StartsWith("/articles", StringComparison.Ordinal))
            .SelectMany(path => path.Value.Operations!)
            .ToList();

        foreach (var (operationType, operation) in articleOperations)
        {
            operation.Security ??= [];

            // POST/PUT are the write endpoints — Azure AD bearer auth only, no
            // API key (that shared secret is the Cloudflare Worker's, for
            // public reads; writes don't go through the Worker). GET stays on
            // the API key as before.
            if (operationType == HttpMethod.Post || operationType == HttpMethod.Put)
            {
                operation.Security.Add(new()
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            }
            else
            {
                operation.Security.Add(new()
                {
                    [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
                });
            }
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

// The actual check lives in ApiSecurity (unit-tested there); this just
// supplies the configured key from the closure.
ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
    ApiSecurity.RequireApiKey(context, next, apiKey);

app.MapGet("/healthz", () => "Healthy");

// Pagination (?limit=&after=) is opt-in: with neither param, behavior is
// byte-for-byte what it always was — the full list, plain array, no new
// response headers — so sitemap.xml/feed.xml/the tag cloud/api-proxy's
// existing SWR cache (all of which call this with no query string) are
// completely unaffected. Only the "load more" infinite-scroll path
// (ui-worker) passes limit/after. See IArticleRepository.GetArticlesPageAsync.
const int MaxArticlesPageSize = 50;

app.MapGet("/articles", async (int? limit, string? after, IArticleRepository articleRepository, HttpContext httpContext) =>
{
    if (limit is null)
    {
        var articles = await articleRepository.GetArticlesAsync();
        return Results.Json(articles);
    }

    var clampedLimit = Math.Clamp(limit.Value, 1, MaxArticlesPageSize);
    var page = await articleRepository.GetArticlesPageAsync(clampedLimit, after);

    httpContext.Response.Headers["X-Has-More"] = page.HasMore ? "true" : "false";
    if (page.NextCursor is not null)
    {
        httpContext.Response.Headers["X-Next-Cursor"] = page.NextCursor;
    }

    return Results.Json(page.Items);
})
    .RequireRateLimiting(ArticlesRateLimiterPolicy)
    .AddEndpointFilter(RequireApiKey);

app.MapGet("/articles/{slug}", async Task<IResult> (string slug, IArticleRepository articleRepository, ILogger<Program> logger) =>
{
    if (string.IsNullOrEmpty(slug))
    {
        return Results.Problem("Invalid slug", statusCode: StatusCodes.Status400BadRequest);
    }

    var article = await articleRepository.GetArticleBySlugAsync(slug);
    if (article is null)
    {
        return Results.Problem("Article not found", statusCode: StatusCodes.Status404NotFound);
    }

    // View counting is best-effort: a failure here shouldn't turn an
    // otherwise-successful article fetch into an error for the reader.
    Article? withView = null;
    try
    {
        withView = await articleRepository.IncrementViewCountAsync(slug);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to increment view count for article {Slug}.", slug);
    }

    return Results.Json(withView ?? article);
})
    .RequireRateLimiting(ArticlesRateLimiterPolicy)
    .AddEndpointFilter(RequireApiKey);

// Write endpoints: Azure AD app-only auth (client-credentials + the
// Articles.Write app role) instead of the API key — per the BRD, this
// replaces manual hand-entry into Cosmos as the normal authoring path.
// No API-key filter here on purpose; the Cloudflare Worker never writes.

app.MapPost("/articles", async Task<IResult> (ArticleWriteRequest request, IArticleRepository articleRepository) =>
{
    var errors = ArticleWriteValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
    }

    var created = await articleRepository.CreateArticleAsync(request);
    if (created is null)
    {
        // CreateArticleAsync returns null only when the slug is already
        // taken — the API doesn't auto-dedupe; per the BRD, the caller
        // retries with a postfixed slug on this response.
        return Results.Problem($"An article with slug '{request.Slug}' already exists.", statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Created($"/articles/{created.Slug}", created);
})
    .RequireRateLimiting(ArticlesWriteRateLimiterPolicy)
    .RequireAuthorization(ArticlesWriteAuthorizationPolicy);

app.MapPut("/articles/{slug}", async Task<IResult> (string slug, ArticleWriteRequest request, IArticleRepository articleRepository) =>
{
    if (string.IsNullOrEmpty(slug))
    {
        return Results.Problem("Invalid slug", statusCode: StatusCodes.Status400BadRequest);
    }

    var errors = ArticleWriteValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
    }

    if (!string.Equals(slug, request.Slug, StringComparison.Ordinal))
    {
        return Results.Problem("The slug in the request body must match the slug in the URL.", statusCode: StatusCodes.Status400BadRequest);
    }

    var updated = await articleRepository.UpdateArticleAsync(slug, request);
    if (updated is null)
    {
        // Update-only, not upsert — per the BRD, PUT never creates.
        return Results.Problem("Article not found", statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Json(updated);
})
    .RequireRateLimiting(ArticlesWriteRateLimiterPolicy)
    .RequireAuthorization(ArticlesWriteAuthorizationPolicy);

app.Run();

// Article, IArticleRepository, CosmosSettings, and InMemoryArticleRepository
// now live in their own files (see Article.cs, IArticleRepository.cs,
// CosmosSettings.cs, InMemoryArticleRepository.cs).

// TODO:
// dotnet ef migrations add InitialCreate
// dotnet ef database update