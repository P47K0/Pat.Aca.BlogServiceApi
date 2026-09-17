# Pat.Aca.BlogServiceApi

The whole blog feature for my personal site: backend API, frontend Workers, and the infra/deployment that ties them together.

## What's in this repo

| Path | Description |
|---|---|
| `Pat.Aca.BlogServiceApi/` | .NET 10 minimal API — reads/writes articles and comments in Cosmos DB |
| `Pat.Aca.BlogCommentsModerationFunction/` | .NET 10 isolated Azure Function — moderates newly-submitted comments (Cosmos DB Change Feed trigger, Cloudflare Workers AI scoring, ACS email alerts) and sweeps up any left stuck |
| `Pat.Aca.BlogServiceApi/Cloudflare/api-proxy/` | Cloudflare Worker: proxies reads/comment submissions to the API, renders Markdown → HTML |
| `Pat.Aca.BlogServiceApi/Cloudflare/ui-worker/` | Cloudflare Worker: the actual blog pages the site serves, including the comment form (Turnstile-protected) |
| `Pat.ACA.BlogServiceTests/` | Unit + integration tests for the API |
| `Pat.Aca.BlogCommentsModerationFunctionTests/` | Unit tests for the moderation Function's decision logic |
| `infra/cosmos-db.bicep` | Cosmos DB account, containers, and least-privilege RBAC roles |
| `infra/comments-function.bicep` | Hosting for the moderation Function (Flex Consumption plan, storage, App Insights) |
| `.github/workflows/deploy-app.yml` | Builds and deploys the API to Azure Container Apps |
| `.github/workflows/cosmos-db.yml` | Applies `cosmos-db.bicep` |
| `.github/workflows/comments-function-infra.yml` | Applies `comments-function.bicep` |
| `.github/workflows/deploy-comments-function.yml` | Builds and deploys the moderation Function's code |

## Stack

- .NET 10 minimal API, deployed as a container to Azure Container Apps
- .NET 10 isolated Azure Function (Flex Consumption plan) for async comment moderation
- Azure Cosmos DB (or the local emulator for dev)
- Cloudflare Workers (TypeScript) for the public-facing site, Cloudflare Turnstile for comment-form bot defense
- Cloudflare Workers AI for LLM-based comment moderation scoring
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

A submitted comment always lands at `status: queued` first. `Pat.Aca.BlogCommentsModerationFunction` picks it up via a Cosmos DB Change Feed trigger, scores it with Cloudflare Workers AI against a configurable prompt (`Moderation__ModerationSystemPrompt`), and — as long as today's `Moderation__DailyModerationQuota` isn't exhausted — patches its status to `unpublished` (the default, for manual review) or `published` (only if the score meets `Moderation__AutoPublishMinScore`, which starts above the maximum possible score so nothing auto-publishes until that's deliberately lowered). Either way, a review-alert email goes out via Azure Communication Services. A comment left `queued` after a scoring failure or exhausted quota is recovered by an hourly sweep, not retried immediately (to avoid hammering a genuinely down dependency).

### See it in action!
blog.koorevaar.com
