@description('Cosmos DB account name')
param accountName string = 'cosmos-koorevaar'

@description('Azure region for the Cosmos DB account')
param location string = resourceGroup().location

@description('Database name')
param databaseName string = 'ArticlesDB'

@description('Container name')
param containerName string = 'Articles'

@description('Partition key path')
param partitionKeyPath string = '/slug'

@description('Throughput for the database/container')
param throughput int = 1000

@description('Principal object ID of the blog service managed identity')
param blogServicePrincipalId string

@description('Principal object ID of a human author who should get Data Explorer read/write access (Cosmos DB Built-in Data Contributor). Optional — leave blank to skip.')
param blogAuthorPrincipalId string = ''

@description('Principal object ID of the Comments moderation Function\'s managed identity. Optional — leave blank until that Function is deployed and its principal id is known (same chicken-and-egg deploy-then-configure ordering as blogServicePrincipalId originally needed).')
param blogCommentsFunctionPrincipalId string = ''

resource account 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: toLower(accountName)
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    publicNetworkAccess: 'Enabled'
    isVirtualNetworkFilterEnabled: false
    enableFreeTier: true
    disableLocalAuth: true
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
    options: {
      throughput: throughput
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: containerName
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: [
          partitionKeyPath
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
  }
}

// Comments container, shared throughput with Articles (same database) --
// rides the account's existing Cosmos free tier (1000 RU/s + 25 GB) rather
// than provisioning new dedicated capacity. Partition key is /articleSlug
// (not /slug, to avoid colliding with Article's own partition key path
// convention while still keying on the same logical value) since the
// dominant read pattern is "all comments for one article".
resource commentsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: 'Comments'
  properties: {
    resource: {
      id: 'Comments'
      partitionKey: {
        paths: [
          '/articleSlug'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
  }
}

// Lease container for the Comments moderation Function's Cosmos DB
// Change Feed trigger -- internal processing checkpoints only, no user
// data. Shares the same database/throughput as everything else here for
// the same free-tier reasons as commentsContainer above. Partition key
// is /id (not /articleSlug) since lease documents are keyed by their own
// id, with no other logical grouping.
resource commentsLeasesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: 'CommentsLeases'
  properties: {
    resource: {
      id: 'CommentsLeases'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource cosmosReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: account
  name: guid(account.id, blogServicePrincipalId, 'Cosmos DB Built-in Data Reader')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000001')
    principalId: blogServicePrincipalId
    scope: account.id
  }
}

// Custom role, additive to cosmosReaderRoleAssignment above: grants only the
// write actions the app actually performs, not the built-in Data
// Contributor role (which also grants upsert/delete the app never uses).
// Originally just "replace" (a Patch — which Cosmos's RBAC model treats as a
// "replace" data action, per the OperationType in the error this closed:
// "does not have the required RBAC permissions to perform action
// [.../items/replace]"), for the view-count increment. "create" added once
// POST /articles (the write API) started calling CreateItemAsync — still no
// delete: that stays human-only via cosmosAuthorRoleAssignment below, per
// the BRD's phased write-API scope (create+update now, delete later once a
// website UI can split that role out).
resource cosmosBlogServiceWriterRoleDefinition 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2024-05-15' = {
  parent: account
  name: guid(account.id, 'BlogServiceApi view-count writer role')
  properties: {
    roleName: 'BlogServiceApi Writer'
    type: 'CustomRole'
    assignableScopes: [
      account.id
    ]
    permissions: [
      {
        dataActions: [
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/replace'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/create'
        ]
      }
    ]
  }
}

resource cosmosBlogServiceWriterRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: account
  name: guid(account.id, blogServicePrincipalId, 'BlogServiceApi view-count writer role')
  properties: {
    roleDefinitionId: cosmosBlogServiceWriterRoleDefinition.id
    principalId: blogServicePrincipalId
    scope: account.id
  }
}

// Comments are a public write surface (any anonymous reader can submit
// one) -- a fundamentally different trust boundary than the AI-only
// Articles write path above. This role is deliberately fenced to the
// Comments container alone via a container-scoped assignableScopes: even
// though it's assigned to the same blogServicePrincipalId managed
// identity as cosmosBlogServiceWriterRoleAssignment, it grants zero access
// to Articles. Read access for the Comments container comes from the
// existing account-wide cosmosReaderRoleAssignment above (Cosmos DB
// Built-in Data Reader is scoped to the whole account, not just
// Articles) -- this role is purely additive for the writes that role
// doesn't cover. Started with "create" only (for the public
// POST /articles/{slug}/comments endpoint); "replace"/"delete" added
// here once the Comments.Moderate endpoints (PATCH to flip
// published/unpublished, DELETE to hard-delete spam) were built --
// same incrementally-grown pattern as cosmosBlogServiceWriterRoleDefinition
// above. The Comments moderation Function runs under its own managed
// identity, not blogServicePrincipalId -- it gets its own assignment
// against this same role definition (cosmosCommentsFunctionWriterRoleAssignment,
// below the leases container), not a change to this permissions list.
resource cosmosCommentsWriterRoleDefinition 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2024-05-15' = {
  parent: account
  name: guid(account.id, 'BlogServiceApi comments writer role')
  properties: {
    roleName: 'BlogServiceApi Comments Writer'
    type: 'CustomRole'
    assignableScopes: [
      '${account.id}/dbs/${database.name}/colls/${commentsContainer.name}'
    ]
    permissions: [
      {
        dataActions: [
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/create'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/replace'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/delete'
        ]
      }
    ]
  }
}

resource cosmosCommentsWriterRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: account
  name: guid(account.id, blogServicePrincipalId, 'BlogServiceApi comments writer role')
  properties: {
    roleDefinitionId: cosmosCommentsWriterRoleDefinition.id
    principalId: blogServicePrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${commentsContainer.name}'
  }
}

// Grants a human author (not the app) Data Explorer read/write, since
// articles are authored by hand directly in Cosmos, never through the API.
// Skipped entirely when blogAuthorPrincipalId is left blank.
resource cosmosAuthorRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogAuthorPrincipalId)) {
  parent: account
  name: guid(account.id, blogAuthorPrincipalId, 'Cosmos DB Built-in Data Contributor')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000002')
    principalId: blogAuthorPrincipalId
    scope: account.id
  }
}

// The three assignments below give the Comments moderation Function's own
// managed identity (a separate principal from blogServicePrincipalId,
// which these don't touch) exactly what it needs -- skipped entirely
// until that Function exists and its principal id is known, same
// optional/blank-to-skip pattern as cosmosAuthorRoleAssignment above.

// Read access to the Comments container specifically, not account-wide
// like cosmosReaderRoleAssignment above -- needed to read the Change
// Feed. Narrower than the API's own reader grant (this Function has no
// legitimate reason to read Articles), a stricter least-privilege
// posture than the API principal gets.
resource cosmosCommentsFunctionReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'Cosmos DB Built-in Data Reader', 'Comments')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000001')
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${commentsContainer.name}'
  }
}

// Same custom writer role blogServicePrincipalId uses on this container
// (see cosmosCommentsWriterRoleDefinition's own doc comment) -- the
// Function needs create+replace for the daily quota counter document and
// replace for patching a comment's status/llmScore after scoring.
resource cosmosCommentsFunctionWriterRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'BlogServiceApi comments writer role')
  properties: {
    roleDefinitionId: cosmosCommentsWriterRoleDefinition.id
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${commentsContainer.name}'
  }
}

// Full read/write on the leases container only -- internal Change Feed
// checkpoints, no user data, so the built-in Data Contributor role is
// fine here rather than a bespoke custom role just for this.
resource cosmosCommentsFunctionLeasesRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'Cosmos DB Built-in Data Contributor', 'CommentsLeases')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000002')
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${commentsLeasesContainer.name}'
  }
}

output cosmosAccountName string = account.name
output cosmosDatabaseName string = database.name
output cosmosContainerName string = container.name
output cosmosCommentsContainerName string = commentsContainer.name
output cosmosCommentsLeasesContainerName string = commentsLeasesContainer.name
output cosmosEndpoint string = account.properties.documentEndpoint
