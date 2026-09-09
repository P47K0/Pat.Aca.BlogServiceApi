// Hosting for Pat.Aca.BlogCommentsModerationFunction. Separate template
// from cosmos-db.bicep (which owns the Cosmos account/containers/RBAC
// role *definitions*) since this is a different resource lifecycle --
// blogCommentsFunctionPrincipalId in cosmos-db.bicep is this template's
// own functionApp's principal id, fed back into a cosmos-db.bicep
// redeploy once this template's output is known (same chicken-and-egg
// ordering blogServicePrincipalId itself needed for blog-service-api).
//
// Flex Consumption, not classic (Linux) Consumption -- confirmed via
// research: Linux Consumption does not support .NET 10 isolated at all,
// and .NET 8/9 support on Azure Functions ends 2026-11-10, so Flex
// Consumption is the only viable choice for a Function built on net10.0
// today. Uses raw ARM resource declarations throughout, matching this
// repo's existing style in cosmos-db.bicep, rather than the Azure
// Verified Modules used in Microsoft's own official Flex Consumption
// samples -- kept consistent with what's already here rather than
// introducing a second IaC style.
//
// Not validated with az/bicep CLI (unavailable in this sandbox, same
// constraint as every other infra file in this repo) -- resource shapes
// (functionAppConfig, identity-based storage/App Insights settings, the
// exact built-in role GUIDs below) were cross-checked against
// Microsoft's own official Flex Consumption sample
// (Azure-Samples/azure-functions-flex-consumption-samples) rather than
// relied on from memory alone, but this is still the one piece of the
// whole Comments feature that most needs a real deployment to confirm.

@description('Azure region for all resources in this template')
param location string = resourceGroup().location

@description('Function App name')
param functionAppName string = 'func-blog-comments-moderation'

@description('Flex Consumption hosting plan name')
param hostingPlanName string = 'plan-blog-comments-moderation'

@description('Storage account name -- suffixed with a deterministic unique string since storage account names must be globally unique and are capped at 24 characters')
param storageAccountName string = 'stcmod${uniqueString(resourceGroup().id)}'

@description('Application Insights resource name')
param appInsightsName string = 'appi-blog-comments-moderation'

@description('Log Analytics workspace name backing Application Insights (workspace-based App Insights is required for new resources)')
param logAnalyticsWorkspaceName string = 'log-blog-comments-moderation'

@description('The Cosmos account\'s document endpoint (cosmos-db.bicep\'s own cosmosEndpoint output) -- used both for this Function\'s own CosmosSettings binding and for the Cosmos DB trigger\'s identity-based connection (see the CosmosDb__accountEndpoint/CosmosDb__credential app settings below)')
param cosmosEndpointUri string

@description('Cosmos database name -- must match cosmos-db.bicep\'s own databaseName')
param cosmosDatabaseName string = 'ArticlesDB'

@description('Cloudflare account id, for the Workers AI scoring calls')
param cloudflareAccountId string

@secure()
@description('Cloudflare API token scoped to Workers AI -- kept secure() so it never appears in deployment logs/ARM history')
param cloudflareApiToken string

@secure()
@description('Azure Communication Services connection string, reusing the user\'s existing resource from their other project, YoutubeChannelGuard')
param acsConnectionString string

@description('The verified ACS sender address')
param acsSenderAddress string

@description('Where the moderation review-alert email is sent')
param acsRecipientEmail string

@description('Cloudflare Workers AI model id -- deployed as a plain app setting so it can be changed later directly in the Function App\'s configuration, without a redeploy')
param moderationModelId string = '@cf/meta/llama-3.2-1b-instruct'

@description('Max comments scored per day')
param dailyModerationQuota int = 25

@description('A comment scoring at/above this auto-publishes instead of landing at Unpublished for review. Deliberately above the max score (5) by default, so nothing auto-publishes until this is explicitly lowered')
param autoPublishMinScore int = 6

@description('System-message instructions sent to the LLM alongside each comment -- deployed as a plain app setting so wording can be tuned directly in the Function App\'s configuration, without a redeploy')
param moderationSystemPrompt string = 'You are a content moderator for a personal blog\'s comment section. Given a reader\'s comment, score how safe it is to publish on a scale of 0 to 5: 0 means definitely do not publish, 5 means definitely fine to publish. Score low for offensive, hateful, or sexual content; commercial spam or advertising; and low-quality garbage (gibberish, irrelevant text, or obvious bot output). Score high for genuine, on-topic reader engagement, even if critical or negative in tone. Respond with ONLY a single JSON object, no other text, in exactly this shape: {"score": <integer 0-5>, "reason": "<one short sentence explaining the score>"}'

var deploymentContainerName = 'app-package'

// allowSharedKeyAccess: false -- identity-only access throughout (the
// Function's system-assigned identity, via the RBAC assignments below),
// same "no shared keys/static secrets where RBAC will do" posture as
// Cosmos's own disableLocalAuth: true in cosmos-db.bicep.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Flex Consumption's own deployment package lives here -- referenced by
// functionAppConfig.deployment.storage below, read via the Function
// App's system-assigned identity (Storage Blob Data Owner, granted
// below), not a connection string/shared key.
resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: deploymentContainerName
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storageAccount.properties.primaryEndpoints.queue
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: storageAccount.properties.primaryEndpoints.table
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING'
          value: 'Authorization=AAD'
        }
        // This Function's own CosmosSettings binding (Program.cs) --
        // EndpointUri/Database, no PrimaryKey, since production always
        // goes through DefaultAzureCredential.
        {
          name: 'CosmosDb__EndpointUri'
          value: cosmosEndpointUri
        }
        {
          name: 'CosmosDb__Database'
          value: cosmosDatabaseName
        }
        // Separate from the two settings above despite the same "CosmosDb"
        // prefix and the same underlying endpoint value -- this pair is
        // read by the Cosmos DB trigger extension's own identity-based
        // connection convention (see CommentModerationFunction's doc
        // comment), a different binding mechanism from this Function's
        // own CosmosSettings class, which happens to share the prefix.
        {
          name: 'CosmosDb__accountEndpoint'
          value: cosmosEndpointUri
        }
        {
          name: 'CosmosDb__credential'
          value: 'managedidentity'
        }
        {
          name: 'Moderation__ModerationModelId'
          value: moderationModelId
        }
        {
          name: 'Moderation__DailyModerationQuota'
          value: string(dailyModerationQuota)
        }
        {
          name: 'Moderation__AutoPublishMinScore'
          value: string(autoPublishMinScore)
        }
        {
          name: 'Moderation__ModerationSystemPrompt'
          value: moderationSystemPrompt
        }
        {
          name: 'CloudflareWorkersAi__AccountId'
          value: cloudflareAccountId
        }
        {
          name: 'CloudflareWorkersAi__ApiToken'
          value: cloudflareApiToken
        }
        {
          name: 'AcsEmail__ConnectionString'
          value: acsConnectionString
        }
        {
          name: 'AcsEmail__SenderAddress'
          value: acsSenderAddress
        }
        {
          name: 'AcsEmail__RecipientEmail'
          value: acsRecipientEmail
        }
      ]
    }
  }
}

// Deployment package storage (blob) -- Flex Consumption reads the
// Function's code from here via the Function's own identity, per
// functionAppConfig.deployment.storage.authentication above.
resource storageBlobDataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Blob Data Owner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// AzureWebJobsStorage (queues/blobs/tables the Functions host itself
// needs for its own bookkeeping) -- identity-based, per the
// AzureWebJobsStorage__*ServiceUri app settings above, not a connection
// string/shared key.
resource storageQueueDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Queue Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageTableDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Table Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Lets the Function publish telemetry to Application Insights via AAD
// (APPLICATIONINSIGHTS_AUTHENTICATION_STRING = Authorization=AAD above)
// instead of the classic instrumentation-key/connection-string-only
// model.
resource monitoringMetricsPublisherRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, functionApp.id, 'Monitoring Metrics Publisher')
  scope: applicationInsights
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Feed this into cosmos-db.bicep's blogCommentsFunctionPrincipalId param
// on a redeploy to actually grant this Function's identity the Cosmos
// RBAC it needs (see that template's own cosmosCommentsFunctionReader/
// Writer/LeasesRoleAssignment resources) -- same chicken-and-egg
// deploy-then-configure ordering blogServicePrincipalId itself needed.
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppName string = functionApp.name
output functionAppDefaultHostname string = functionApp.properties.defaultHostName
