#!/usr/bin/env bash
# Starts one ephemeral runner for the Azure Container Apps job (infra/github-runner-job.bicep): asks
# GitHub for a just-in-time runner configuration, then runs exactly one job and exits. The job's
# replica ends with it, so every job gets a clean container.
#
# Environment (set by the job):
#   GITHUB_PAT      token that may register runners (ACA secret)
#   RUNNER_SCOPE    repo | org
#   GITHUB_OWNER    user or organisation
#   GITHUB_REPO     repository name (scope repo only)
#   RUNNER_LABELS   comma-separated, e.g. self-hosted,Linux
#   GITHUB_API_URL  optional, default https://api.github.com
set -euo pipefail

: "${GITHUB_PAT:?GITHUB_PAT is not set}"
: "${GITHUB_OWNER:?GITHUB_OWNER is not set}"
: "${RUNNER_LABELS:?RUNNER_LABELS is not set}"
api=${GITHUB_API_URL:-https://api.github.com}

case "${RUNNER_SCOPE:-repo}" in
  repo) : "${GITHUB_REPO:?GITHUB_REPO is not set}"; scope_path="repos/$GITHUB_OWNER/$GITHUB_REPO" ;;
  org)  scope_path="orgs/$GITHUB_OWNER" ;;
  *)    echo "RUNNER_SCOPE must be repo or org" >&2; exit 1 ;;
esac

# The replica's hostname is unique per execution; the suffix covers a retried replica.
name="aca-$(hostname)-$RANDOM"
body=$(jq -nc --arg name "$name" --arg labels "$RUNNER_LABELS" \
  '{name: $name, runner_group_id: 1, labels: ($labels | split(",")), work_folder: "_work"}')

response=$(curl -sS --fail-with-body --retry 3 --retry-all-errors -X POST \
  -H "Authorization: Bearer $GITHUB_PAT" \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  "$api/$scope_path/actions/runners/generate-jitconfig" -d "$body")
jit=$(jq -er '.encoded_jit_config' <<<"$response")

# The token is only needed to get the configuration. Drop it before the runner starts, so the job's
# steps (which inherit the runner's environment) can't read it.
unset GITHUB_PAT response

echo "Starting ephemeral runner $name for $scope_path with labels $RUNNER_LABELS"
exec ./run.sh --jitconfig "$jit"
