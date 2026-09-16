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

@description('Principal object ID of the KnowledgeBase-Writer app registration\'s service principal — a dedicated identity (deliberately separate from blogServicePrincipalId) Claude authenticates as to write embeddings directly into the KnowledgeBase container for the AI chat assistant project, bypassing the .NET API for this container entirely. Optional — leave blank to skip provisioning the KnowledgeBase container/role until ready.')
param knowledgeBaseWriterPrincipalId string = ''

@description('Principal object ID of the KnowledgeBase-Reader app registration\'s service principal — a dedicated, deliberately narrower identity the public-facing assistant-worker Cloudflare Worker authenticates as to query the KnowledgeBase container at retrieval time. Distinct from knowledgeBaseWriterPrincipalId: this identity is reachable by any site visitor\'s question indirectly, so it gets read+executeQuery only, never create/replace. Optional — leave blank to skip provisioning this role until the Worker\'s retrieval path is ready.')
param knowledgeBaseReaderPrincipalId string = ''

// EnableNoSQLVectorSearch (below) and knowledgeBaseContainer (further down)
// are deployed together in this one template for simplicity, but the
// capability can take up to ~15 minutes to actually propagate per
// Microsoft's own docs, and the container's vector policy depends on it
// already being active. If knowledgeBaseContainer fails on a deployment's
// first attempt, that's why — wait a few minutes and re-run cosmos-db.yml
// unchanged. Incremental mode means anything that already succeeded
// (including this capability flag) won't be redone, only the still-missing
// resource gets retried.
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
    capabilities: [
      {
        name: 'EnableNoSQLVectorSearch'
      }
    ]
  }
}

// Second database, deliberately separate from ArticlesDB, with no
// throughput specified at this resource's own level. Vector search is not
// supported in a database that has shared throughput provisioned at the
// database level, and ArticlesDB already does (Articles/Comments/
// CommentsLeases all ride its 1000 RU/s shared pool) -- giving
// knowledgeBaseContainer its own dedicated throughput isn't enough on its
// own if it stays a child of ArticlesDB, since the restriction is about
// the database it lives in, not just the specific container's own
// throughput setting. Found this the hard way: PR #30 originally put
// knowledgeBaseContainer directly under ArticlesDB with its own dedicated
// autoscale throughput, deployed cleanly for the capability/role
// resources but the container itself failed with Cosmos's own
// "shared throughput" BadRequest, confirmed against Microsoft's docs
// 2026-09-12 after the fact. AssistantDB has no options.throughput of its
// own -- only its one container (knowledgeBaseContainer) is billed, at
// that container's own dedicated autoscale rate.
resource assistantDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  parent: account
  name: 'AssistantDB'
  properties: {
    resource: {
      id: 'AssistantDB'
    }
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
      // -1 enables per-item TTL without forcing a default expiration on
      // everything -- real comments never set their own ttl field, so they
      // stay permanent. CosmosModerationQuotaStore's daily counter documents
      // (a synthetic doc sharing this container, see its own doc comment)
      // are the one thing here that sets ttl explicitly, so they self-expire
      // instead of accumulating one tiny document per day forever, found in
      // production 2026-09-11 (a "yesterday" and "today" doc already sitting
      // there, nothing had ever cleaned either up).
      defaultTtl: -1
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

// Lease container for ArticleCountSyncFunction's Cosmos DB Change Feed
// trigger (blog-post counter, see the backlog item of that name) --
// same shape/reasoning as commentsLeasesContainer above, just a separate
// container since a Change Feed trigger's lease checkpoints are scoped
// to one specific source container (Articles here, Comments there) and
// can't share a lease container between the two.
resource articlesLeasesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: 'ArticlesLeases'
  properties: {
    resource: {
      id: 'ArticlesLeases'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

// KnowledgeBase container for the AI chat assistant project (a separate
// future project sharing this Cosmos account, see the backlog item "AI
// chat/assistant that answers questions about Patrick" for the full design)
// -- a RAG knowledge store holding article-derived chunks and "about me"
// profile facts side by side, distinguished by a sourceType field, not the
// Articles container plus a new field. Cosmos's vector embedding/index
// policy is immutable once a container is created and cannot be added to
// an existing one (confirmed against Microsoft's current docs, 2026-09-12)
// -- that's the whole reason this is a brand new container rather than an
// embedding field bolted onto Articles.
//
// Embeddings are bge-m3 (1024-dimensional, chosen for multilingual
// English/Dutch retrieval), cosine distance. quantizedFlat over flat: flat
// caps out at 505 dimensions, well under bge-m3's 1024, so it isn't even an
// option here; quantizedFlat and diskANN both fall back to an exact full
// scan below 1,000 indexed vectors anyway (this container is nowhere near
// that for the foreseeable future), and quantizedFlat is the simpler of
// the two for a container this small.
//
// Dedicated autoscale throughput, set on this container directly. This
// alone isn't sufficient though -- see assistantDatabase's own comment
// above for why this container also has to live in its own database, not
// ArticlesDB, even with its own dedicated throughput set here. Minimum
// autoscale tier (max 1000 RU/s, floor 100) chosen deliberately over the
// flat 400 RU/s manual minimum: this container's usage (rare writes,
// occasional reads from chat queries) is idle-dominated, and autoscale
// bills for whatever it actually scaled to each hour rather than a flat
// rate -- meaningfully cheaper here despite the 1.5x per-RU multiplier.
//
// Partition key is /sourceType (only two values today, "article"/"profile")
// -- a deliberately low-cardinality choice that would be a poor fit at real
// scale, but this container is expected to stay in the dozens-to-low-
// hundreds of documents range, where partition fan-out cost is negligible;
// it was picked for the logical grouping it gives (scoping a future query
// to just profile facts) over strict partitioning best practice.
//
// Deploy-order note: this resource's vector policy requires the account's
// EnableNoSQLVectorSearch capability (see the account resource above) to
// have actually propagated first, which can take up to ~15 minutes per
// Microsoft's own docs. Both changes are deployed together in this one
// template for simplicity, but if this specific resource fails on a
// deployment's first attempt, that's why -- wait a few minutes and re-run
// cosmos-db.yml unchanged; Incremental mode means anything that already
// succeeded won't be redone, only this still-missing resource gets
// retried.
resource knowledgeBaseContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: assistantDatabase
  name: 'KnowledgeBase'
  properties: {
    resource: {
      id: 'KnowledgeBase'
      partitionKey: {
        paths: [
          '/sourceType'
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
          {
            path: '/embedding/*'
          }
        ]
        vectorIndexes: [
          {
            path: '/embedding'
            type: 'quantizedFlat'
          }
        ]
      }
      vectorEmbeddingPolicy: {
        vectorEmbeddings: [
          {
            path: '/embedding'
            dataType: 'float32'
            distanceFunction: 'cosine'
            dimensions: 1024
          }
        ]
      }
    }
    options: {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

// Custom role for the KnowledgeBase-Writer app registration's service
// principal (see knowledgeBaseWriterPrincipalId above) -- container-scoped
// via assignableScopes, same fencing pattern as
// cosmosCommentsWriterRoleDefinition below, so this identity gets zero
// access to Articles/Comments/CommentsLeases even though it's a distinct
// principal from blogServicePrincipalId. Grants create+replace+read+
// executeQuery: read so the same identity can point-read what it just
// wrote without needing a second role, create for new embeddings, replace
// for re-embedding a piece of content whose source changed. executeQuery
// is a separate data action from read -- point-reads (GET /docs/{id})
// worked fine without it, but an actual SQL query (which VectorDistance()
// retrieval fundamentally depends on) returned a 403 until this was added,
// found via testing 2026-09-12, not caught beforehand since nothing had
// tried a real query yet at that point. Skipped entirely when
// knowledgeBaseWriterPrincipalId is blank, same optional/blank-to-skip
// pattern as blogAuthorPrincipalId.
resource cosmosKnowledgeBaseWriterRoleDefinition 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2024-05-15' = if (!empty(knowledgeBaseWriterPrincipalId)) {
  parent: account
  name: guid(account.id, 'KnowledgeBase writer role')
  properties: {
    roleName: 'KnowledgeBase Writer'
    type: 'CustomRole'
    assignableScopes: [
      '${account.id}/dbs/${assistantDatabase.name}/colls/${knowledgeBaseContainer.name}'
    ]
    permissions: [
      {
        dataActions: [
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/create'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/replace'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/read'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/executeQuery'
        ]
      }
    ]
  }
}

resource cosmosKnowledgeBaseWriterRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(knowledgeBaseWriterPrincipalId)) {
  parent: account
  name: guid(account.id, knowledgeBaseWriterPrincipalId, 'KnowledgeBase writer role')
  properties: {
    roleDefinitionId: cosmosKnowledgeBaseWriterRoleDefinition.id
    principalId: knowledgeBaseWriterPrincipalId
    scope: '${account.id}/dbs/${assistantDatabase.name}/colls/${knowledgeBaseContainer.name}'
  }
}

// Custom role for the KnowledgeBase-Reader app registration's service
// principal (see knowledgeBaseReaderPrincipalId above) -- same
// container-scoped fencing as cosmosKnowledgeBaseWriterRoleDefinition, but
// deliberately narrower: read+executeQuery only, no create/replace. This
// identity backs assistant-worker's live retrieval path (a public-facing
// Cloudflare Worker, indirectly reachable by any site visitor's question),
// a materially different trust boundary than KnowledgeBase-Writer (used
// only from an authenticated authoring session) -- least-privilege here
// means it can never modify KnowledgeBase content even if the Worker were
// compromised. executeQuery is required, not optional, for this identity:
// VectorDistance() retrieval is a SQL query, and (per
// cosmosKnowledgeBaseWriterRoleDefinition's own comment) that's a separate
// data action from plain read. Skipped entirely when
// knowledgeBaseReaderPrincipalId is blank, same optional/blank-to-skip
// pattern as knowledgeBaseWriterPrincipalId.
resource cosmosKnowledgeBaseReaderRoleDefinition 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2024-05-15' = if (!empty(knowledgeBaseReaderPrincipalId)) {
  parent: account
  name: guid(account.id, 'KnowledgeBase reader role')
  properties: {
    roleName: 'KnowledgeBase Reader'
    type: 'CustomRole'
    assignableScopes: [
      '${account.id}/dbs/${assistantDatabase.name}/colls/${knowledgeBaseContainer.name}'
    ]
    permissions: [
      {
        dataActions: [
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/items/read'
          'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/executeQuery'
        ]
      }
    ]
  }
}

resource cosmosKnowledgeBaseReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(knowledgeBaseReaderPrincipalId)) {
  parent: account
  name: guid(account.id, knowledgeBaseReaderPrincipalId, 'KnowledgeBase reader role')
  properties: {
    roleDefinitionId: cosmosKnowledgeBaseReaderRoleDefinition.id
    principalId: knowledgeBaseReaderPrincipalId
    scope: '${account.id}/dbs/${assistantDatabase.name}/colls/${knowledgeBaseContainer.name}'
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

// The four assignments below give the Comments moderation Function's own
// managed identity (a separate principal from blogServicePrincipalId,
// which these don't touch) exactly what it needs -- skipped entirely
// until that Function exists and its principal id is known, same
// optional/blank-to-skip pattern as cosmosAuthorRoleAssignment above.

// Read access to the Comments container specifically, not account-wide
// like cosmosReaderRoleAssignment above -- needed to read the Change
// Feed. Narrower than the API's own reader grant, a stricter
// least-privilege posture than the API principal gets.
resource cosmosCommentsFunctionReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'Cosmos DB Built-in Data Reader', 'Comments')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000001')
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${commentsContainer.name}'
  }
}

// Read-only access to the Articles container, scoped to just that
// container -- CosmosArticleContextProvider looks up an article's summary
// (never anything else) so the moderation LLM can judge whether a comment
// is actually on-topic, something it can't do from the comment's own text
// alone (see IArticleContextProvider's own doc comment). This Function
// was originally reader-only on Comments; added once that gap was found
// in production on 2026-09-10/11 -- still read-only, still scoped to one
// container, same least-privilege posture as every other grant here.
resource cosmosCommentsFunctionArticlesReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'Cosmos DB Built-in Data Reader', 'Articles')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000001')
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${container.name}'
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

// Full read/write on ArticleCountSyncFunction's own lease container only --
// same reasoning as cosmosCommentsFunctionLeasesRoleAssignment above.
// blogCommentsFunctionPrincipalId already has Data Reader on Articles
// itself (cosmosCommentsFunctionArticlesReaderRoleAssignment above,
// originally added for CosmosArticleContextProvider) -- that's also
// exactly what ArticleCountSyncFunction's own recompute query needs, so no
// separate reader grant is required for it.
resource cosmosCommentsFunctionArticlesLeasesRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(blogCommentsFunctionPrincipalId)) {
  parent: account
  name: guid(account.id, blogCommentsFunctionPrincipalId, 'Cosmos DB Built-in Data Contributor', 'ArticlesLeases')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000002')
    principalId: blogCommentsFunctionPrincipalId
    scope: '${account.id}/dbs/${database.name}/colls/${articlesLeasesContainer.name}'
  }
}

output cosmosAccountName string = account.name
output cosmosDatabaseName string = database.name
output cosmosContainerName string = container.name
output cosmosCommentsContainerName string = commentsContainer.name
output cosmosCommentsLeasesContainerName string = commentsLeasesContainer.name
output cosmosArticlesLeasesContainerName string = articlesLeasesContainer.name
output cosmosAssistantDatabaseName string = assistantDatabase.name
output cosmosKnowledgeBaseContainerName string = knowledgeBaseContainer.name
output cosmosEndpoint string = account.properties.documentEndpoint
