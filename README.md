# Pat.Aca.BlogServiceApi

The whole blog feature for my personal site: backend API, frontend Workers, and the infra/deployment that ties them together. Three parts of it are AI-driven: comments are auto-moderated by an LLM before they ever reach manual review, an AI chat assistant answers visitor questions grounded in the blog's own content, and the blog's search box is semantic (embedding-based) rather than keyword matching — both the assistant and search share the same underlying knowledge base, built almost entirely from the articles themselves.

## What's in this repo

| Path | Description |
|---|---|
| `Pat.Aca.BlogServiceApi/` | .NET 10 minimal API — reads/writes articles and comments in Cosmos DB |
| `Pat.Aca.BlogCommentsModerationFunction/` | .NET 10 isolated Azure Function — moderates newly-submitted comments (Cosmos DB Change Feed trigger, Cloudflare Workers AI scoring, ACS email alerts) and sweeps up any left stuck |
| `Pat.Aca.BlogServiceApi/Cloudflare/api-proxy/` | Cloudflare Worker: proxies reads/comment submissions to the API, renders Markdown → HTML |
| `Pat.Aca.BlogServiceApi/Cloudflare/ui-worker/` | Cloudflare Worker: the actual blog pages the site serves, including the comment form (Turnstile-protected) and the semantic search page |
| `Pat.Aca.BlogServiceApi/Cloudflare/assistant-worker/` | Cloudflare Worker: RAG-based AI chat assistant (`POST /ask`) and semantic search (`GET /search`), both retrieving from the `KnowledgeBase` Cosmos container — see [AI assistant & semantic search](#ai-assistant--semantic-search) below |
| `Pat.ACA.BlogServiceTests/` | Unit + integration tests for the API |
| `Pat.Aca.BlogCommentsModerationFunctionTests/` | Unit tests for the moderation Function's decision logic |
| `infra/cosmos-db.bicep` | Cosmos DB account, containers (including the AI assistant's `KnowledgeBase` vector container), and least-privilege RBAC roles |
| `infra/comments-function.bicep` | Hosting for the moderation Function (Flex Consumption plan, storage, App Insights) |
| `.github/workflows/deploy-app.yml` | Builds and deploys the API to Azure Container Apps |
| `.github/workflows/cosmos-db.yml` | Applies `cosmos-db.bicep` |
| `.github/workflows/comments-function-infra.yml` | Applies `comments-function.bicep` |
| `.github/workflows/deploy-comments-function.yml` | Builds and deploys the moderation Function's code |

The three Cloudflare Workers (`api-proxy`, `ui-worker`, `assistant-worker`) have no GitHub Actions deploy workflow — Cloudflare's own Git integration watches this repo and deploys each on push instead.

## Stack

- .NET 10 minimal API, deployed as a container to Azure Container Apps
- .NET 10 isolated Azure Function (Flex Consumption plan) for async, **AI-scored comment moderation** — see [Comment moderation flow](#comment-moderation-flow)
- Azure Cosmos DB (or the local emulator for dev), including a dedicated vector-search container (`KnowledgeBase`) backing the AI assistant and search
- Cloudflare Workers (TypeScript) for the public-facing site, Cloudflare Turnstile for comment-form bot defense
- Cloudflare Workers AI: LLM scoring for comment moderation, plus embedding/generation for the AI chat assistant and semantic search — see [AI assistant & semantic search](#ai-assistant--semantic-search)
- Azure Communication Services for moderation review-alert emails

## Running the API locally

Requires the [Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/local-emulator) running on `localhost:8081`.

```bash
dotnet run --project Pat.Aca.BlogServiceApi
```

Swagger UI is available at `/swagger` in development.

## Tests

```bash
dotnet test
```

## Running the workflows on your own computer

Every workflow uses `runs-on: ${{ vars.RUNNER || 'ubuntu-latest' }}`. With the repository variable `RUNNER` set to `self-hosted` (Settings → Secrets and variables → Actions → Variables), jobs run on your own computer instead of using GitHub Actions minutes. Delete the variable to go back to GitHub-hosted runners.

On Windows with Docker Desktop running:

```powershell
powershell -ExecutionPolicy Bypass -File .\runner\start-runner.ps1
```

The script builds `runner/Dockerfile` and starts one fresh container per job. The image holds what the jobs would otherwise download every time: the actions the workflows use, the .NET SDK, Azure CLI with Bicep and the containerapp extension, and zip. NuGet packages are kept between jobs in the Docker volume `blog-runner-nuget`, and the Deploy workflow builds on Docker Desktop's own layer cache. The first build of the image takes a while (Azure/functions-action alone is about 100 MB); later builds are cached. On first start it asks for a fine-grained GitHub token for this repository with **Administration: Read and write**. The token is saved encrypted under `%LOCALAPPDATA%\blog-runner\` and never goes into a container. Stop with Ctrl+C. Each container gets the Docker socket for the Deploy workflow's image build.

## Runners in Azure Container Apps

More capacity than one computer, without GitHub-hosted minutes: ephemeral runners in a Container Apps Job. The KEDA `github-runner` scaler looks at the queued workflow runs of a repository (or organisation) every 30 seconds, starts one container per queued job (at most 4 at once), and each container registers as a just-in-time runner, runs that one job and exits. Nothing runs, and nothing is billed, while the queue is empty.

Nothing here is specific to this repo. The image (`runner/aca`) and the job (`infra/github-runner-job.bicep`) take the owner, repository and labels as parameters, so each repo that wants ACA runners gets its own job in the same Container Apps environment (`ACA_ENVIRONMENT_NAME`, here `koorevaar-env`), from the same image.

**Set up**

1. Create a fine-grained token that covers the target repo(s): **Administration: Read and write** (a runner registers itself with it) and **Actions: Read** (the scaler reads the queue). One token can cover several repos. For scope `org` use the organisation's equivalent permissions.
2. In this repo add it as the Actions secret `RUNNER_GITHUB_PAT`. The workflow stores it as an ACA secret; it is not in the image.
3. Run the **GitHub Runner on ACA** workflow (Actions → Run workflow). Leave owner and repo blank for this repo, or fill in another repo of yours. It builds and pushes `<DOCKERHUB_USERNAME>/github-runner` and creates or updates the job `gh-runner-<repo>`. Run it again for every repo that should get runners, or to change the labels or `max_parallel`. It needs Docker for the image build, so run it on the laptop runner (`RUNNER=self-hosted`), not on an ACA runner.
4. In the target repo set the variable (here `RUNNER`) to `self-hosted`, as for the laptop runner.

**How it fits with the laptop runner.** Both register with the labels `self-hosted` and `Linux`, so a workflow's `runs-on` is the same for both and GitHub gives a queued job to whichever idle runner takes it first. An idle laptop takes it within a second or two, before the scaler's next check, so ACA mostly starts for the overflow, or for everything while the laptop is off. An ACA runner that finds its job already taken waits until the job's replica timeout (30 minutes by default, `replicaTimeoutSeconds`) and is billed meanwhile; lower it if your jobs are short. Expect 30 to 60 seconds before a job starts on ACA (the scaler's check, the container start, the registration).

**What the image holds.** git, curl, jq, zip/unzip, the GitHub CLI, rclone, Azure CLI, Node with wrangler (also in the runner's tool cache, so `actions/setup-node` doesn't download it), and Chromium for Playwright (`PLAYWRIGHT_BROWSERS_PATH=/ms-playwright`); optionally the .NET SDK (workflow input `dotnet`). The actions listed in the `PREFETCH_ACTIONS` build argument are cached in the image too, so a job doesn't download them. Each of these is a build argument in `runner/aca/Dockerfile`; switch off what the target repos don't use to keep the image small.

**What a workflow must do differently on ACA.** The container has no sudo ("no new privileges"), and no Docker (no privileged containers). So:
- `npx playwright install --with-deps chromium` fails, because `--with-deps` calls sudo. The libraries are already in the image: run it without `--with-deps` when `sudo -n true` fails.
- `curl ... | sudo bash` installers fail. Guard them with `command -v <tool> >/dev/null || ...` for tools the image has (rclone), or add the tool to the Dockerfile.
- A workflow that builds an image with the local Docker daemon, like this repo's Deploy workflow, has to stay on the laptop.

Only use this with private repositories: anyone who can run a workflow in a repo can run code on its self-hosted runners. The token is dropped from the runner's environment before the job starts, but it is a secret of the job in ACA.

## API

| Endpoint | Auth |
|---|---|
| `GET /healthz` | none |
| `GET /articles`, `GET /articles/{slug}` | API key (`X-Api-Key`) |
| `POST /articles`, `PUT /articles/{slug}` | Azure AD bearer token (`Articles.Write` app role) |
| `GET /articles/{slug}/comments`, `POST /articles/{slug}/comments` | API key (`X-Api-Key`) |
| `GET /articles/{slug}/comments/all`, `PATCH /articles/{slug}/comments/{commentId}`, `DELETE /articles/{slug}/comments/{commentId}` | Azure AD bearer token (`Comments.Moderate` app role) |

Reads are for the Cloudflare Workers serving the public site. Article writes are used by Claude Code, on request, to create and update articles. Comment submission is public (anonymous readers, via the site's comment form) but rate-limited and Turnstile-protected; comment moderation (listing every status, publishing/unpublishing, deleting spam) has no admin UI — Claude Code drives it directly via the `Comments.Moderate` endpoints on request, same as it drives article writes.

### Comment moderation flow

Every comment is moderated by AI, not by a human reviewer first — there's no queue a person clears before publication decisions happen. A submitted comment always lands at `status: queued` first. `Pat.Aca.BlogCommentsModerationFunction` picks it up via a Cosmos DB Change Feed trigger, scores it with Cloudflare Workers AI against a configurable prompt (`Moderation__ModerationSystemPrompt`), and — as long as today's `Moderation__DailyModerationQuota` isn't exhausted — patches its status to `unpublished` (the default, for manual review) or `published` (only if the score meets `Moderation__AutoPublishMinScore`, which starts above the maximum possible score so nothing auto-publishes until that's deliberately lowered). Either way, a review-alert email goes out via Azure Communication Services. A comment left `queued` after a scoring failure or exhausted quota is recovered by an hourly sweep, not retried immediately (to avoid hammering a genuinely down dependency).

## AI assistant & semantic search

`assistant-worker` (a separate Cloudflare Worker, see its own README for the phase-by-phase build-up) is a small RAG stack sitting in front of the blog's own content, backing two visitor-facing features:

- **`POST /ask`** — the AI chat assistant. Embeds the question, retrieves the nearest chunks from the `KnowledgeBase` Cosmos container (with a KV-backed semantic cache in front of live retrieval), and generates a grounded answer via `@cf/google/gemma-3-12b-it`, restricted by prompt guardrails to what those chunks actually say. Rate-limited and Turnstile-protected; conversations aren't logged by default.
- **`GET /search`** — the blog's search box (rendered by `ui-worker`, server-to-server call only). Same retrieval path, scoped to `sourceType: "article"` chunks and deduped to one result per article — no generation involved, just ranked relevance.

`KnowledgeBase` (`infra/cosmos-db.bicep`) is a Cosmos container purpose-built for vector search: partitioned by `sourceType` (`article` chunks and hand-written `profile` facts about me side by side), indexed for cosine-distance search over `bge-m3` (1024-dimensional) embeddings. Content is **embedded at authoring time, not by this Worker** — an article is chunked into one summary + one embedding per paragraph and written directly into `KnowledgeBase` (bypassing the .NET API entirely) whenever Claude Code creates or updates an article, using a `KnowledgeBase-Writer` identity kept deliberately separate from the article-writing role. `assistant-worker` only ever embeds text at query time (the same `bge-m3` model via Workers AI), and reads via a narrower, read-only `KnowledgeBase-Reader` identity.

### See it in action!
https://blog.koorevaar.com
