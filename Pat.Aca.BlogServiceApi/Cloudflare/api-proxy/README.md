# blog-service-worker

Cloudflare Worker sitting in front of `Pat.Aca.BlogServiceApi`. The frontend
never calls the API or holds credentials directly — it calls this Worker,
which:

1. Proxies `GET /articles` and `GET /articles/{slug}` to the API, mirroring
   its paths exactly.
2. Attaches the shared secret as the `X-Api-Key` header on the outbound call.
3. Renders each article's `content` field from Markdown to HTML server-side
   before responding — the frontend never sees raw Markdown.

Error responses from the API (RFC 7807 `problem+json` for 401/404/429/500)
are passed through unchanged.

Runs on the free Cloudflare Workers plan, on the default `*.workers.dev`
subdomain — no custom domain needed since nothing calls this Worker directly
in a browser address bar.

## Setup

```bash
npm install
npx wrangler login          # one-time, opens a browser to authorize this machine
wrangler secret put ARTICLES_API_KEY   # paste the same value as the API's ApiKey / the ARTICLES_API_KEY GitHub secret
```

The API's base URL is **not** a secret — it's set as a plain `[vars]` entry in
`wrangler.toml` (`API_BASE_URL`), the Worker equivalent of an `appsettings.json`
value.

## Deploy

Cloudflare's Git integration watches this repo and deploys automatically on
push — no GitHub Actions workflow needed for this Worker. `npm run deploy`
still works for a manual/local deploy (e.g. testing a change before pushing):

```bash
npm run deploy
```

## Known follow-ups (not in scope for this first pass)

- **CORS** is currently wide open (`Access-Control-Allow-Origin: *`) since no
  frontend exists yet. Restrict it to the frontend's real origin once that's
  built.
- **Edge caching** of the rendered HTML (e.g. via the Cache API) is planned
  future work, not implemented yet.
- **Custom domain**: could later be attached under `koorevaar.com` (e.g.
  `api.koorevaar.com`) via a `routes` entry in `wrangler.toml` — not needed
  today.
