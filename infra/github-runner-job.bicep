@description('Name of the existing Container Apps environment to run the job in (same resource group)')
param environmentName string = 'koorevaar-env'

@description('Azure region; must match the environment')
param location string = resourceGroup().location

@description('Name of the Container Apps Job. One job per GitHub repository (or organisation), e.g. gh-runner-sites-template')
@maxLength(32)
param jobName string

@description('Runner image from runner/aca/Dockerfile, e.g. <dockerhub-user>/github-runner:latest. Public: it holds no secrets, the token only reaches the container at runtime.')
param image string

@description('repo = runners for one repository, org = runners for an organisation')
@allowed([
  'repo'
  'org'
])
param runnerScope string = 'repo'

@description('GitHub user or organisation that owns the repository')
param githubOwner string

@description('Repository name. Required for scope repo, ignored for scope org')
param githubRepo string = ''

@description('Runner labels, comma-separated. Must match the labels of the other runners (e.g. a laptop) so one runs-on value works for both. The scaler only starts a runner for queued jobs that these labels can serve.')
param runnerLabels string = 'self-hosted,Linux'

@description('GitHub token for the scaler (reads the queued workflow runs) and the runner (registers itself). Stored as an ACA secret.')
@secure()
param githubPat string

@description('GitHub API URL; only changes for GitHub Enterprise Server')
param githubApiUrl string = 'https://api.github.com'

@description('Most runners (and so jobs) that run at the same time')
@minValue(1)
@maxValue(20)
param maxParallel int = 4

@description('Seconds between the scaler\'s checks of the queue. Together with the container start, this is the cold start of a job.')
@minValue(10)
param pollingIntervalSeconds int = 30

@description('Seconds a runner may live. A runner that never gets a job (the laptop took it first) idles, and is billed, until this ends; a longer job needs a longer timeout.')
@minValue(60)
param replicaTimeoutSeconds int = 1800

@description('vCPU per runner')
param cpu string = '2.0'

@description('Memory per runner')
param memory string = '4Gi'

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: environmentName
}

var scalerMetadata = union({
  githubAPIURL: githubApiUrl
  owner: githubOwner
  runnerScope: runnerScope
  labels: runnerLabels
  targetWorkflowQueueLength: '1'
}, runnerScope == 'repo' ? { repos: githubRepo } : {})

resource runnerJob 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Event'
      replicaTimeout: replicaTimeoutSeconds
      replicaRetryLimit: 0
      secrets: [
        {
          name: 'github-pat'
          value: githubPat
        }
      ]
      eventTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
        scale: {
          minExecutions: 0
          maxExecutions: maxParallel
          pollingInterval: pollingIntervalSeconds
          rules: [
            {
              name: 'github-runner'
              type: 'github-runner'
              metadata: scalerMetadata
              auth: [
                {
                  secretRef: 'github-pat'
                  triggerParameter: 'personalAccessToken'
                }
              ]
            }
          ]
        }
      }
    }
    template: {
      containers: [
        {
          name: 'runner'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            {
              name: 'GITHUB_PAT'
              secretRef: 'github-pat'
            }
            {
              name: 'GITHUB_API_URL'
              value: githubApiUrl
            }
            {
              name: 'RUNNER_SCOPE'
              value: runnerScope
            }
            {
              name: 'GITHUB_OWNER'
              value: githubOwner
            }
            {
              name: 'GITHUB_REPO'
              value: githubRepo
            }
            {
              name: 'RUNNER_LABELS'
              value: runnerLabels
            }
          ]
        }
      ]
    }
  }
}

output jobName string = runnerJob.name
