# blog-ui-worker

Cloudflare Worker that serves the blog's public UI — server-rendered with
[Hono](https://hono.dev)'s JSX, no client-side framework or build-time SSG.

```
Browser → ui-worker (this Worker, blog.koorevaar.com)
            → api-proxy Worker (blog-service-worker, *.workers.dev)
                → Pat.Aca.BlogServiceApi
```

It never talks to `Pat.Aca.BlogServiceApi` or holds the shared API key
directly — it fetches JSON from the **api-proxy** Worker, which already
attaches `X-Api-Key` and renders each article's Markdown `content` to HTML.
This Worker's only job is routing + page templates.

## Pages

- `GET /` — all articles, newest-first, with a tag cloud built from whatever
  tags appear across them.
- `GET /tags/:tag` — articles filtered to one tag. There's no dedicated
  tag-filter endpoint on the API, so this fetches the full article list from
  api-proxy and filters here.
- `GET /articles/:slug` — full article, with its already-rendered HTML
  content injected directly (see the comment in `ArticleDetail.tsx` for the
  trust rationale — content is hand-authored in Cosmos, never user-submitted).

Styling is [Tailwind via the Play CDN](https://tailwindcss.com/docs/installation/play-cdn)
(`?plugins=typography` for the article `prose` styling) — no build step, no
local npm dependency for styling, consistent with api-proxy's
no-local-dev-testing minimalism.

## Setup

```bash
npm install
npx wrangler login          # one-time, opens a browser to authorize this machine
```

Before deploying, edit `wrangler.toml`'s `API_PROXY_BASE_URL` to api-proxy's
actual deployed URL (its `*.workers.dev` subdomain, or a custom domain if it's
later given one) — the scaffolded value is a placeholder.

## Deploy

```bash
npm run deploy
```

Deploys to `blog.koorevaar.com` (see the `routes` entry in `wrangler.toml`) —
requires `koorevaar.com` to already be added as a zone in this Cloudflare
account.

## Known follow-ups (not in scope for this first pass)

- **Edge caching**: every request re-fetches from api-proxy — no caching at
  this layer yet, same deferred-future-work status as api-proxy's own caching.
- **Series support**: the API's data model has `seriesName`/`seriesOrder`
  planned but not yet implemented, so there's no series navigation UI here
  either.
- **No local dev testing set up** (matches api-proxy's choice) — `wrangler
  dev` isn't configured with a `.dev.vars` override for `API_PROXY_BASE_URL`.
