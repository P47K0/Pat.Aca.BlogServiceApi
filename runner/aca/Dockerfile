# The GitHub Actions runner for Azure Container Apps (README "Runners in Azure Container Apps").
# Unlike runner/Dockerfile (this repo's own workflows, on your computer), this image is not tied to a
# repository: entrypoint.sh registers it for whatever repo or organisation the job's environment
# variables name, so one image serves every repo that gets a job from infra/github-runner-job.bicep.
#
# Jobs can land on this runner or on a laptop runner with the same labels, so it carries the tooling
# the workflows would otherwise download in every job. Each tool is a build argument; switch off what
# the target repos don't use to keep the image small (a smaller image starts faster).
#
# The PREFETCH_ACTIONS build argument lists the actions a job would otherwise download from GitHub at
# its start; they are cached in the image. Keep it in step with the `uses:` lines of the target repos.
#
# No Docker here: ACA does not allow privileged containers, so a workflow that builds an image with
# the local Docker daemon has to stay on a runner that has one.
FROM ghcr.io/actions/actions-runner:latest

ARG INSTALL_NODE=true
ARG NODE_MAJOR=22
ARG INSTALL_PLAYWRIGHT=true
ARG INSTALL_DOTNET=false
ARG DOTNET_CHANNEL=10.0
ARG PREFETCH_ACTIONS="actions/checkout@v4 actions/setup-node@v4 actions/upload-artifact@v4 azure/login@v2"

USER root

# What the workflows expect from ubuntu-latest: git, curl, jq, zip/unzip, xz (Node tarballs) and the
# GitHub CLI, plus Azure CLI for azure/login and the az steps.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip xz-utils zip \
 && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
 && apt-get update && apt-get install -y --no-install-recommends gh \
 && curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash \
 && rm -rf /var/lib/apt/lists/*

# rclone, the static binary from rclone's own site. Workflows that run "curl .../install.sh | sudo bash"
# can't on ACA (no sudo, see the Playwright note below), so they guard it with `command -v rclone`.
RUN arch=$(dpkg --print-architecture) \
 && curl -fsSL --retry 3 "https://downloads.rclone.org/rclone-current-linux-$arch.zip" -o /tmp/rclone.zip \
 && unzip -q /tmp/rclone.zip -d /tmp/rclone \
 && install -m 0755 /tmp/rclone/rclone-*-linux-$arch/rclone /usr/local/bin/rclone \
 && rm -rf /tmp/rclone /tmp/rclone.zip \
 && rclone version

# Node (for wrangler, npm ci and the site builds) and wrangler itself.
RUN if [ "$INSTALL_NODE" = "true" ]; then \
      curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash - \
      && apt-get install -y --no-install-recommends nodejs \
      && npm install -g wrangler \
      && rm -rf /var/lib/apt/lists/* ~/.npm; \
    fi

# The same Node in the runner's tool cache, where actions/setup-node looks before it downloads one
# (that download took 30 to 40 seconds per job). The newest NODE_MAJOR.x when the image is built.
ENV RUNNER_TOOL_CACHE=/opt/hostedtoolcache AGENT_TOOLSDIRECTORY=/opt/hostedtoolcache
RUN if [ "$INSTALL_NODE" = "true" ]; then \
      arch=$(dpkg --print-architecture | sed 's/amd64/x64/') \
      && version=$(curl -fsSL https://nodejs.org/dist/index.json | jq -r --arg p "v${NODE_MAJOR}." '[.[] | select(.version | startswith($p))][0].version | ltrimstr("v")') \
      && mkdir -p /opt/hostedtoolcache/node/$version/$arch \
      && curl -fsSL "https://nodejs.org/dist/v$version/node-v$version-linux-$arch.tar.xz" \
         | tar -xJ --strip-components=1 -C /opt/hostedtoolcache/node/$version/$arch \
      && touch /opt/hostedtoolcache/node/$version/$arch.complete; \
    fi \
 && mkdir -p /opt/hostedtoolcache && chown -R runner:runner /opt/hostedtoolcache

# Chromium for Playwright, with the system libraries it needs. A job's own "playwright install
# --with-deps chromium" fails on ACA (it calls sudo, which "no new privileges" blocks): jobs should skip
# --with-deps there, the libraries are already here. The browsers go to a shared folder the
# runner user can write to: a project whose @playwright/test wants another Chromium revision than the
# one baked in downloads it there instead of failing.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN if [ "$INSTALL_PLAYWRIGHT" = "true" ]; then \
      [ "$INSTALL_NODE" = "true" ] || { echo "INSTALL_PLAYWRIGHT needs INSTALL_NODE=true"; exit 1; }; \
      npm install -g playwright \
      && playwright install --with-deps chromium \
      && chmod -R a+rwX "$PLAYWRIGHT_BROWSERS_PATH" \
      && rm -rf /var/lib/apt/lists/* ~/.npm; \
    fi

# The .NET SDK in /usr/share/dotnet, where actions/setup-dotnet looks for it (see runner/Dockerfile).
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
ENV PATH=$DOTNET_ROOT:$PATH
RUN if [ "$INSTALL_DOTNET" = "true" ]; then \
      curl -fsSL --retry 5 --retry-all-errors -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
      && bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT" \
      && rm /tmp/dotnet-install.sh \
      && chown -R runner:runner "$DOTNET_ROOT"; \
    fi

USER runner

# The actions of PREFETCH_ACTIONS in the runner's action archive cache: the runner copies a job's
# actions from here instead of downloading them (a download that once hung for 100 seconds). It reads
# ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE/<owner>_<repo>/<commit>.tar.gz. Each tag is resolved to its commit
# at build time; a tag that moves on later is a cache miss and downloaded as usual.
ENV ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE=/home/runner/action-archive-cache
RUN set -eu; \
    for action in $PREFETCH_ACTIONS; do \
      repo=${action%@*}; tag=${action#*@}; \
      refs=$(git ls-remote "https://github.com/$repo" "refs/tags/$tag" "refs/tags/$tag^{}"); \
      sha=$(echo "$refs" | awk '/\^\{\}$/ {print $1; found=1} END {if (!found) exit 1}' \
            || echo "$refs" | awk 'NR==1 {print $1}'); \
      test -n "$sha"; \
      dir="$ACTIONS_RUNNER_ACTION_ARCHIVE_CACHE/$(echo "$repo" | tr / _)"; \
      mkdir -p "$dir"; \
      curl -fsSL --retry 5 --retry-all-errors -o "$dir/$sha.tar.gz" \
        "https://codeload.github.com/$repo/tar.gz/$sha"; \
      echo "$action -> $sha"; \
    done

COPY --chown=runner:runner --chmod=755 entrypoint.sh /home/runner/entrypoint.sh
ENTRYPOINT ["/home/runner/entrypoint.sh"]
