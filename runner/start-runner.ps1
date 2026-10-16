<#
.SYNOPSIS
  Runs the GitHub Actions jobs of this repository on this computer, one fresh Docker container per job.

.DESCRIPTION
  README "Running the workflows on your own computer". Copied from the sites-template runner, minus its Node
  and Playwright parts. Needs Docker Desktop and a fine-grained GitHub token for this repository only, with the permission "Administration: Read and write" (the API that registers a
  runner needs it). The token stays on this computer: in $env:BLOG_RUNNER_TOKEN, or asked once and saved
  encrypted for your Windows account (Export-Clixml) under %LOCALAPPDATA%\blog-runner\. It never goes
  into a container: each container only gets a one-job runner registration (a "just-in-time" config).

  Each container gets Docker Desktop's socket, because the Deploy workflow builds and pushes the API image.
  A job can therefore start and stop containers on this computer; only run workflows from this repository.

  Jobs go to this runner while the repository variable RUNNER is "self-hosted". Stop with Ctrl+C (a job
  that is running then fails).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\runner\start-runner.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\runner\start-runner.ps1 -Parallel 3
  Three jobs at once, each still in its own fresh container. Every running job needs about 1 to 2 GB
  of memory.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\runner\start-runner.ps1 -Repo P47K0/other-project
  A runner for another repository (on a personal account a runner belongs to one repository): one copy
  of this script per repository, side by side. The token then needs those repositories too.
#>
param(
  [string]$Repo = "P47K0/Pat.Aca.BlogServiceApi",
  [string]$Image = "blog-runner",
  # How many jobs run at the same time: one runner loop per slot.
  [ValidateRange(1, 8)][int]$Parallel = 1,
  # Only for trying the script against a stand-in for GitHub's API.
  [string]$ApiUrl = "https://api.github.com"
)

$ErrorActionPreference = "Stop"

function Get-Token {
  if ($env:BLOG_RUNNER_TOKEN) { return $env:BLOG_RUNNER_TOKEN }
  $file = $null
  if ($env:LOCALAPPDATA) { $file = Join-Path $env:LOCALAPPDATA "blog-runner\token.xml" }
  if ($file -and (Test-Path $file)) { return (Import-Clixml $file).GetNetworkCredential().Password }
  $secure = Read-Host "Fine-grained GitHub token for $Repo (Administration: Read and write)" -AsSecureString
  $credential = New-Object System.Management.Automation.PSCredential("runner", $secure)
  if ($file) {
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    $credential | Export-Clixml $file
    Write-Host "Saved, encrypted for your Windows account, in $file (delete it to enter a new token)."
  }
  return $credential.GetNetworkCredential().Password
}

docker info --format "{{.ServerVersion}}" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Docker is not running: start Docker Desktop first." }

# Rebuilt on every start, so the image follows runner/Dockerfile (cached, so quick when nothing changed).
# The context is the runner folder: the image needs nothing else from the repository.
docker build --pull -t $Image -f (Join-Path $PSScriptRoot "Dockerfile") $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw "The runner image did not build." }

$token = Get-Token
$session = [guid]::NewGuid().ToString("N")

# One runner loop per slot, each in a background job: register a one-job runner, run it in a fresh
# container, repeat. The container label ties the containers to this run of the script, for the clean-up.
$slotLoop = {
  param($Repo, $Image, $Token, $Slot, $ApiUrl, $Session)
  # Not "Stop" here: in a job, Windows PowerShell turns docker's stderr lines into errors, and those must
  # not end the slot. The API call stops on its own errors.
  $ErrorActionPreference = "Continue"
  $headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
  }
  while ($true) {
    # Repository and slot in the name, so slots and copies of this script for other repositories never clash.
    $name = "laptop-" + $Repo.Split("/")[1] + "-$Slot-" + (Get-Date -Format "yyyyMMdd-HHmmss")
    $body = @{ name = $name; runner_group_id = 1; labels = @("self-hosted", "Linux"); work_folder = "_work" } | ConvertTo-Json
    try {
      $jit = Invoke-RestMethod -Method Post -Uri "$ApiUrl/repos/$Repo/actions/runners/generate-jitconfig" `
        -Headers $headers -Body $body -ContentType "application/json" -ErrorAction Stop
    } catch {
      $status = 0
      if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
      if ($status -in 401, 403, 404) {
        throw "GitHub refused the token ($status): it needs access to $Repo with Administration: Read and write. A saved token is in %LOCALAPPDATA%\blog-runner\token.xml; delete that file to enter a new one."
      }
      Write-Warning "[$Slot] Could not register a runner ($($_.Exception.Message)); trying again in 30 seconds."
      Start-Sleep -Seconds 30
      continue
    }

    Write-Output "$(Get-Date -Format T)  $name is waiting for a job"
    $started = Get-Date
    # Docker Desktop's socket, for the Deploy workflow's image build (docker/build-push-action). It belongs to
    # root:root inside the container, so the runner user joins group 0 to use it.
    # blog-runner-nuget keeps NuGet packages between jobs (see runner/Dockerfile).
    docker run --rm --name $name --label "blog-runner.session=$Session" `
      -v /var/run/docker.sock:/var/run/docker.sock --group-add 0 `
      -v blog-runner-nuget:/home/runner/.nuget/packages `
      $Image ./run.sh --jitconfig $jit.encoded_jit_config |
      ForEach-Object { "[$Slot] $_" }
    if ($LASTEXITCODE -ne 0 -and ((Get-Date) - $started).TotalSeconds -lt 30) {
      Write-Warning "[$Slot] The runner container stopped right away (exit code $LASTEXITCODE); trying again in 30 seconds."
      Start-Sleep -Seconds 30
    }
  }
}

$jobs = @(1..$Parallel | ForEach-Object {
  Start-Job -Name "blog-runner-$_" -ScriptBlock $slotLoop -ArgumentList $Repo, $Image, $token, $_, $ApiUrl, $session
})
Write-Host "$Parallel runner slot(s) started for $Repo. Stop with Ctrl+C."
try {
  while ($true) {
    # As plain lines: the runner's and docker's stderr arrive as error records, which must not stop this loop.
    $jobs | Receive-Job -ErrorAction Continue 2>&1 | ForEach-Object { "$_" }
    $failed = $jobs | Where-Object { $_.State -eq "Failed" } | Select-Object -First 1
    if ($failed) {
      $failed | Receive-Job -ErrorAction SilentlyContinue
      throw "Runner slot $($failed.Name) stopped: $($failed.ChildJobs[0].JobStateInfo.Reason.Message)"
    }
    Start-Sleep -Seconds 1
  }
} finally {
  # Also on Ctrl+C: stop every slot and remove its container (a job that is running then fails).
  $jobs | Stop-Job -ErrorAction SilentlyContinue
  $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
  $containers = docker ps -aq --filter "label=blog-runner.session=$session"
  if ($containers) { docker rm -f $containers | Out-Null }
  Write-Host "Runner slots stopped."
}
