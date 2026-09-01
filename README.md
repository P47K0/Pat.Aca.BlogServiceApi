# Pat.Aca.BlogServiceApi

The whole blog feature for my personal site: backend API, frontend Workers, and the infra/deployment that ties them together.

## What's in this repo

| Path | Description |
|---|---|
| `Pat.Aca.BlogServiceApi/` | .NET 10 minimal API — reads/writes articles in Cosmos DB |
| `Pat.Aca.BlogServiceApi/Cloudflare/api-proxy/` | Cloudflare Worker: proxies reads to the API, renders Markdown → HTML |
| `Pat.Aca.BlogServiceApi/Cloudflare/ui-worker/` | Cloudflare Worker: the actual blog pages the site serves |
| `Pat.ACA.BlogServiceTests/` | Unit + integration tests for the API |
| `infra/cosmos-db.bicep` | Cosmos DB account, container, and least-privilege RBAC roles |
| `.github/workflows/deploy-app.yml` | Builds and deploys the API to Azure Container Apps |
| `.github/workflows/cosmos-db.yml` | Applies `cosmos-db.bicep` |

## Stack

- .NET 10 minimal API, deployed as a container to Azure Container Apps
- Azure Cosmos DB (or the local emulator for dev)
- Cloudflare Workers (TypeScript) for the public-facing site

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

Reads are for the Cloudflare Workers serving the public site. Writes are used by Claude Code, on request, to create and update articles.
