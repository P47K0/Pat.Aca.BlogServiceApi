# The self-hosted GitHub Actions runner (README "Running the workflows on your own computer").
# start-runner.ps1 builds this image and starts one container per job, so every job gets a clean
# machine, as on GitHub's own runners.
#
# On top of GitHub's runner image (which already has the docker CLI and buildx): what the workflows
# expect from ubuntu-latest. Azure CLI for azure/login and the az deployment / az containerapp steps,
# zip/unzip for Azure/functions-action, and the .NET SDK (see below).
FROM ghcr.io/actions/actions-runner:latest

USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip zip \
 && curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash \
 && rm -rf /var/lib/apt/lists/*

# The actions the workflows use, downloaded once at image build. Every job runs in a fresh container,
# so without this each job downloads them again, and the runner gives up on a slow download after
# 100 seconds (Azure/functions-action is a large tarball). The runner reads
# ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE/<owner>_<repo>/<sha>.tar.gz before downloading. Keep this list
# in step with the `uses:` lines in .github/workflows. A tag that has moved since this layer was built
# is a cache miss and is downloaded as before; `docker build --no-cache` refreshes them.
ENV ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE=/home/runner/action-archive-cache
RUN set -eu; \
    for action in \
      Azure/functions-action@v1 \
      actions/checkout@v4 \
      actions/setup-dotnet@v4 \
      azure/login@v2 \
      docker/build-push-action@v6 \
      docker/login-action@v3 \
      docker/setup-buildx-action@v3; \
    do \
      repo=${action%@*}; tag=${action#*@}; \
      refs=$(git ls-remote "https://github.com/$repo" "refs/tags/$tag" "refs/tags/$tag^{}"); \
      sha=$(echo "$refs" | awk '/\^\{\}$/ {print $1; found=1} END {if (!found) exit 1}' \
            || echo "$refs" | awk 'NR==1 {print $1}'); \
      dir="$ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE/$(echo "$repo" | tr / _)"; \
      mkdir -p "$dir"; \
      curl -fsSL --retry 5 --retry-all-errors -o "$dir/$sha.tar.gz" \
        "https://codeload.github.com/$repo/tar.gz/$sha"; \
      echo "$action -> $sha"; \
    done; \
    chown -R runner:runner "$ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE"

# The .NET SDK in /usr/share/dotnet, where it is on ubuntu-latest and where actions/setup-dotnet
# installs. That folder belongs to root in this image, so setup-dotnet failed as the runner user
# ("mkdir: cannot create directory '/usr/share/dotnet': Permission denied"). With the SDK here,
# setup-dotnet finds the version already installed; owned by runner, so it can still add a newer
# patch. Below the action cache, so changing this layer doesn't download the actions again.
# Keep the channel in step with dotnet-version in .github/workflows.
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
ENV PATH=$DOTNET_ROOT:$PATH
RUN curl -fsSL --retry 5 --retry-all-errors -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" \
 && rm /tmp/dotnet-install.sh \
 && chown -R runner:runner "$DOTNET_ROOT" \
 && dotnet --version

USER runner
