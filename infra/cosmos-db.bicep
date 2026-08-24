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

resource cosmosReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: account
  name: guid(account.id, blogServicePrincipalId, 'Cosmos DB Built-in Data Reader')
  properties: {
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', account.name, '00000000-0000-0000-0000-000000000001')
    principalId: blogServicePrincipalId
    scope: account.id
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

output cosmosAccountName string = account.name
output cosmosDatabaseName string = database.name
output cosmosContainerName string = container.name
output cosmosEndpoint string = account.properties.documentEndpoint
