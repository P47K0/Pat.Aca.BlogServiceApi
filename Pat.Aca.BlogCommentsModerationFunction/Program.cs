using Azure.Communication.Email;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Pat.Aca.BlogCommentsModerationFunction;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    // Traces, requests, dependencies and logs stay on. Metrics are off: between
    // 2026-09-25 and 2026-10-25 AppMetrics (runtime, Kestrel, HTTP server and
    // process meters) and AppPerformanceCounters were 85% of the workspace's
    // ingested volume, ~35 MB/day growing to ~130 MB/day as the Change Feed and
    // timer functions were added: roughly EUR 0.30 a day on a blog that nothing
    // alerts on. The Drop view discards every instrument before export.
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .WithMetrics(metrics => metrics.AddView(_ => MetricStreamConfiguration.Drop))
        .UseAzureMonitorExporter(options =>
        {
            options.EnablePerformanceCounters = false;
            options.EnableStandardMetrics = false;
        });
}

// Plain-POCO-singleton config binding throughout, same convention as the
// sibling API project (not IOptions<T>) -- a missing/incomplete section
// falls back to each settings class's own property defaults.
var cosmosSettings = builder.Configuration.GetSection("CosmosDb").Get<CosmosSettings>() ?? new CosmosSettings();
var moderationSettings = builder.Configuration.GetSection("Moderation").Get<ModerationSettings>() ?? new ModerationSettings();
var cloudflareSettings = builder.Configuration.GetSection("CloudflareWorkersAi").Get<CloudflareWorkersAiSettings>() ?? new CloudflareWorkersAiSettings();
var acsSettings = builder.Configuration.GetSection("AcsEmail").Get<AcsEmailSettings>() ?? new AcsEmailSettings();
var apiProxySettings = builder.Configuration.GetSection("ApiProxy").Get<ApiProxySettings>() ?? new ApiProxySettings();
var analyticsSettings = builder.Configuration.GetSection("CloudflareAnalytics").Get<CloudflareAnalyticsSettings>() ?? new CloudflareAnalyticsSettings();

builder.Services.AddSingleton(moderationSettings);
builder.Services.AddSingleton(cloudflareSettings);
builder.Services.AddSingleton(acsSettings);
builder.Services.AddSingleton(apiProxySettings);
builder.Services.AddSingleton(analyticsSettings);

// One CosmosClient/Container for the whole Function, shared by
// CosmosModerationQuotaStore and CommentStatusWriter -- see either
// class's own doc comment for why this is built here rather than each
// constructing its own client. Same emulator-cert-bypass/Gateway-mode/
// DefaultAzureCredential branching as the sibling API project's
// repositories.
var cosmosClientOptions = new CosmosClientOptions();
CosmosClient cosmosClient;

if (!string.IsNullOrEmpty(cosmosSettings.PrimaryKey))
{
    var isLocalEmulator = Uri.TryCreate(cosmosSettings.EndpointUri, UriKind.Absolute, out var endpoint)
        && endpoint.IsLoopback;

    if (isLocalEmulator)
    {
        cosmosClientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
        cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
    }

    cosmosClient = new CosmosClient(cosmosSettings.EndpointUri, cosmosSettings.PrimaryKey, cosmosClientOptions);
}
else
{
    cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
    cosmosClient = new CosmosClient(
        accountEndpoint: cosmosSettings.EndpointUri,
        tokenCredential: new DefaultAzureCredential(),
        clientOptions: cosmosClientOptions);
}

var commentsContainer = cosmosClient.GetDatabase(cosmosSettings.Database).GetContainer("Comments");
builder.Services.AddSingleton(commentsContainer);

// Not registered as a bare Container singleton -- DI can't disambiguate
// two singletons of the same type by name, and commentsContainer above
// already claims that slot for CosmosModerationQuotaStore/
// CommentStatusWriter. Captured via closure in a factory instead, so
// CosmosArticleContextProvider gets the right Container without any risk
// of the two accidentally swapping.
var articlesContainer = cosmosClient.GetDatabase(cosmosSettings.Database).GetContainer("Articles");
builder.Services.AddSingleton<IArticleContextProvider>(sp =>
    new CosmosArticleContextProvider(articlesContainer, sp.GetRequiredService<ILogger<CosmosArticleContextProvider>>()));

// Shares the same articlesContainer reference as IArticleContextProvider
// above via closure, same reasoning as that registration's own comment --
// no second Container/client for the same container.
builder.Services.AddSingleton<IArticleCountRepository>(sp => new CosmosArticleCountRepository(articlesContainer));
builder.Services.AddSingleton<IMostViewedArticleRepository>(sp => new CosmosMostViewedArticleRepository(articlesContainer));
builder.Services.AddSingleton<IArticleListRepository>(sp => new CosmosArticleListRepository(articlesContainer));
builder.Services.AddSingleton<IArticleViewCountWriter>(sp => new CosmosArticleViewCountWriter(articlesContainer));
builder.Services.AddSingleton<IViewCountWatermarkStore>(sp => new CosmosViewCountWatermarkStore(commentsContainer));
builder.Services.AddSingleton<IPublishedCommentsRepository>(sp => new CosmosPublishedCommentsRepository(commentsContainer));

builder.Services.AddSingleton(sp => new EmailClient(acsSettings.ConnectionString));

builder.Services.AddHttpClient<IModerationScorer, CloudflareWorkersAiScorer>();
builder.Services.AddSingleton<IModerationNotifier, AcsModerationNotifier>();
builder.Services.AddSingleton<IModerationQuotaStore, CosmosModerationQuotaStore>();
builder.Services.AddHttpClient<IArticleCountPublisher, ApiProxyArticleCountPublisher>();
builder.Services.AddHttpClient<IMostViewedArticlePublisher, ApiProxyMostViewedArticlePublisher>();
builder.Services.AddHttpClient<IArticleListPublisher, ApiProxyArticleListPublisher>();
builder.Services.AddHttpClient<IArticleViewSource, AnalyticsEngineArticleViewSource>();
builder.Services.AddHttpClient<IArticleDetailPublisher, ApiProxyArticleDetailPublisher>();
builder.Services.AddHttpClient<IPublishedCommentsPublisher, ApiProxyPublishedCommentsPublisher>();
builder.Services.AddSingleton<CommentStatusWriter>();
builder.Services.AddSingleton<CommentModerationProcessor>();
builder.Services.AddSingleton<StuckCommentSweeper>();
builder.Services.AddSingleton<ArticleCountSyncProcessor>();
builder.Services.AddSingleton<MostViewedSyncProcessor>();
builder.Services.AddSingleton<ArticleListSyncProcessor>();
builder.Services.AddSingleton<ViewCountFlushProcessor>();
builder.Services.AddSingleton<ArticleDetailSyncProcessor>();
builder.Services.AddSingleton<CommentsSyncProcessor>();

builder.Build().Run();
