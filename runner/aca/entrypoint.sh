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
#   RUNNER_IDLE_TIMEOUT_SECONDS  optional, default 300; 0 = wait for a job until the replica timeout
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

# The scaler starts a runner for every queued job it sees, but another runner (the laptop) may take
# that job first. A runner without a job would then wait, and be billed, until the replica timeout.
# GitHub hands a queued job to an idle runner within seconds, so a runner that has not started one
# after RUNNER_IDLE_TIMEOUT_SECONDS is surplus: stop it and end the execution successfully. The wait is
# long enough that a job queued a little later in the same workflow (behind a `needs:`) still finds it.
idle=${RUNNER_IDLE_TIMEOUT_SECONDS:-300}
log=$(mktemp)

killtree() {
  local child
  for child in $(pgrep -P "$1" || true); do killtree "$child"; done
  kill -TERM "$1" 2>/dev/null || true
}

# The runner's diagnostic lines ("[RUNNER ...]", "[WORKER ...]": ~7,500 lines, 3 MB per job) go to the
# temp file only. Every console line is billed as a Log Analytics row, and GitHub keeps the job's own log.
# The idle check below still reads them from the file; a failed run prints the tail of it.
./run.sh --jitconfig "$jit" > >(tee "$log" | grep --line-buffered -Ev '^\[(RUNNER|WORKER) ') 2>&1 &
runner=$!

watchdog=""
if [ "$idle" -gt 0 ]; then
  (
    sleep "$idle"
    # The runner prints "Running job: <name>" when it accepts one; its Worker process exists while it runs.
    if ! grep -q 'Running job:' "$log" && ! pgrep -f Runner.Worker >/dev/null; then
      echo "No job after ${idle}s, another runner probably took it. Stopping."
      touch "$log.idle"
      killtree "$runner"
    fi
  ) &
  watchdog=$!
fi

code=0
wait "$runner" || code=$?
[ -z "$watchdog" ] || killtree "$watchdog"
if [ "$code" -ne 0 ] && [ ! -e "$log.idle" ]; then
  echo "Runner exited with code $code. Last diagnostic lines:"
  tail -n 200 "$log"
fi
[ ! -e "$log.idle" ] || exit 0
exit "$code"
