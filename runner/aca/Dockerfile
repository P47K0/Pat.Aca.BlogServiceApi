# The GitHub Actions runner for Azure Container Apps (README "Runners in Azure Container Apps").
# Unlike runner/Dockerfile (this repo's own workflows, on your computer), this image is not tied to a
# repository: entrypoint.sh registers it for whatever repo or organisation the job's environment
# variables name, so one image serves every repo that gets a job from infra/github-runner-job.bicep.
#
# Jobs can land on this runner or on a laptop runner with the same labels, so it carries the tooling
# the workflows would otherwise download in every job. Each tool is a build argument; switch off what
# the target repos don't use to keep the image small (a smaller image starts faster).
#
# No Docker here: ACA does not allow privileged containers, so a workflow that builds an image with
# the local Docker daemon has to stay on a runner that has one.
FROM ghcr.io/actions/actions-runner:latest

ARG INSTALL_NODE=true
ARG NODE_MAJOR=22
ARG INSTALL_PLAYWRIGHT=true
ARG INSTALL_DOTNET=false
ARG DOTNET_CHANNEL=10.0

USER root

# What the workflows expect from ubuntu-latest, plus Azure CLI for azure/login and the az steps.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl git jq unzip zip \
 && curl -fsSL https://aka.ms/InstallAzureCLIDeb | bash \
 && rm -rf /var/lib/apt/lists/*

# Node (for wrangler, npm ci and the site builds) and wrangler itself.
RUN if [ "$INSTALL_NODE" = "true" ]; then \
      curl -fsSL "https://deb.nodesource.com/setup_${NODE_MAJOR}.x" | bash - \
      && apt-get install -y --no-install-recommends nodejs \
      && npm install -g wrangler \
      && rm -rf /var/lib/apt/lists/* ~/.npm; \
    fi

# Chromium for Playwright, with the system libraries it needs. The browsers go to a shared folder the
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

COPY --chown=runner:runner --chmod=755 entrypoint.sh /home/runner/entrypoint.sh

USER runner
ENTRYPOINT ["/home/runner/entrypoint.sh"]
