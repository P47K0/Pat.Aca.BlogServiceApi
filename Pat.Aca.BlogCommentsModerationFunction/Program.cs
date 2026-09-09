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
using OpenTelemetry;
using Pat.Aca.BlogCommentsModerationFunction;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

// Plain-POCO-singleton config binding throughout, same convention as the
// sibling API project (not IOptions<T>) -- a missing/incomplete section
// falls back to each settings class's own property defaults.
var cosmosSettings = builder.Configuration.GetSection("CosmosDb").Get<CosmosSettings>() ?? new CosmosSettings();
var moderationSettings = builder.Configuration.GetSection("Moderation").Get<ModerationSettings>() ?? new ModerationSettings();
var cloudflareSettings = builder.Configuration.GetSection("CloudflareWorkersAi").Get<CloudflareWorkersAiSettings>() ?? new CloudflareWorkersAiSettings();
var acsSettings = builder.Configuration.GetSection("AcsEmail").Get<AcsEmailSettings>() ?? new AcsEmailSettings();

builder.Services.AddSingleton(moderationSettings);
builder.Services.AddSingleton(cloudflareSettings);
builder.Services.AddSingleton(acsSettings);

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

builder.Services.AddSingleton(sp => new EmailClient(acsSettings.ConnectionString));

builder.Services.AddHttpClient<IModerationScorer, CloudflareWorkersAiScorer>();
builder.Services.AddSingleton<IModerationNotifier, AcsModerationNotifier>();
builder.Services.AddSingleton<IModerationQuotaStore, CosmosModerationQuotaStore>();
builder.Services.AddSingleton<CommentStatusWriter>();
builder.Services.AddSingleton<CommentModerationProcessor>();

builder.Build().Run();
