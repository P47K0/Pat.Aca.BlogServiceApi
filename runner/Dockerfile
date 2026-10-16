# The self-hosted GitHub Actions runner (README "Running the workflows on your own computer").
# start-runner.ps1 builds this image and starts one container per job, so every job gets a clean
# machine, as on GitHub's own runners.
#
# On top of GitHub's runner image (which already has the docker CLI and buildx): what the workflows
# expect from ubuntu-latest. Azure CLI for azure/login and the az deployment / az containerapp steps,
# zip/unzip for Azure/functions-action. .NET comes from actions/setup-dotnet at job time.
FROM ghcr.io/actions/actions-runner:latest

USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip zip \
 && curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash \
 && rm -rf /var/lib/apt/lists/*

USER runner
