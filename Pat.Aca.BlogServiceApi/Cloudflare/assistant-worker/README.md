# blog-assistant-worker

Cloudflare Worker behind the "AI chat/assistant that answers questions about
Patrick" feature. Sibling to `api-proxy` and `ui-worker` under `Cloudflare/`,
built the same way (its own `package.json`/`wrangler.toml`, no shared code).

Full design (RAG architecture, semantic cache, guardrails) lives in the
project's backlog item, not duplicated here. This is currently just a scaffold
(`GET /healthz`) — built up in small, separately reviewable commits per the
project's own conventions:

1. Scaffold.
2. Workers AI + KV bindings.
3. Semantic-cache lookup: `POST /ask` embeds the question and checks the
   cache.
4. Cache-miss path (this commit): queries the `KnowledgeBase` Cosmos
   container directly via its REST API (no Node Cosmos SDK in a Worker
   runtime) as the KnowledgeBase-Reader identity, merges/re-sorts results
   client-side (one query per `sourceType` partition — Cosmos's REST API
   can't serve a cross-partition `ORDER BY`), and populates the cache.
5. Prompt assembly with scoping guardrails + generation call (this commit):
   `POST /ask` now returns a real generated `answer`, grounded in the
   retrieved `chunks`, via `@cf/google/gemma-3-12b-it` behind a
   response-shape adapter (`src/lib/generation.ts`). Guardrail wording is
   worth a deliberate read in that file before this ever goes live — it's
   the one piece of this Worker that speaks on your behalf.
6. Return JSON — no UI yet (the chat widget itself is a later phase).
7. Phase 6, rate limiting: per-IP fixed-window limit on `POST /ask`
   (`src/lib/rate-limit.ts`), KV-backed under a new key prefix in the
   existing `ASSISTANT_CACHE` namespace, checked before any embedding/
   retrieval/generation spend.
8. Phase 6, Turnstile: `POST /ask` now requires a `turnstileToken` field,
   verified server-side (`src/lib/turnstile.ts`) against the same
   `blog.koorevaar.com` widget `ui-worker` already uses for comments.
   Stubbed in ahead of the chat widget itself (Phase 7) — nothing can supply
   a real token until that widget exists, so every real request 403s for
   now, which is expected.
9. Phase 6, conversation logging: not stored by default; an `allowLogging`
   field in the request body opts a conversation in, and a cheap regex/
   keyword scan (`src/lib/conversation-log.ts`) forces logging regardless
   for an input that looks like abuse or a prompt-injection attempt. One KV
   key per logged entry (90-day `expirationTtl`), same `ASSISTANT_CACHE`
   namespace.
10. Phase 5, cache invalidation: `POST /internal/invalidate-cache`
    (`src/lib/cache-invalidation.ts`), guarded by a shared secret (see
    `wrangler.toml`), called by ACA right after a successful article
    create/update — bumps a global `content-updated-at` KV key. Every
    semantic-cache entry cached before that bump is treated as stale on its
    next lookup (`src/lib/semantic-cache.ts`) and dropped from storage —
    coarse, whole-cache invalidation by design, not a per-article reverse
    index.
11. `GET /internal/embeddings-count` (`src/lib/embeddings-count.ts`),
    guarded by its own shared secret, for a "documents indexed" style
    counter on the site's homepage. Internal-use-only by design — called by
    a Worker, not a browser — and KV-cached for an hour so a public,
    high-traffic page doesn't cost a live Cosmos round trip on every view.
12. `GET /search` (`src/lib/article-search.ts`), reusing the same
    KnowledgeBase embeddings for blog search — retrieval + ranking only, no
    LLM generation. Scoped to `sourceType: "article"` chunks (a profile fact
    has no article page to link to), deduped down to one result per
    `sourceSlug` since an article's summary+paragraph chunks commonly
    co-occur in the same query's results. Server-to-server only, guarded by
    its own shared secret — called by `ui-worker`'s search page, never a
    visitor's browser directly.

## Setup

```bash
npm install
wrangler login   # one-time, not done in this sandbox
```

`ASSISTANT_CACHE`'s KV namespace is already created and wired into
`wrangler.toml`. `KNOWLEDGEBASE_READER_TENANT_ID`/`_CLIENT_ID`/
`_CLIENT_SECRET` are **not** in `wrangler.toml` — set all three via the
dashboard's Settings -> Variables and Secrets (as "Secret", not the
plain-text variant — see `wrangler.toml`'s own comment for why) or
`wrangler secret put <NAME>`, once the `KnowledgeBase-Reader` app
registration exists (create it by hand in the personal Entra tenant, same as
`KnowledgeBase-Writer` — see `infra/cosmos-db.bicep`'s
`knowledgeBaseReaderPrincipalId`) and `cosmos-db.yml` has been re-run with
its principal ID.

`TURNSTILE_SECRET_KEY` is set the same off-`wrangler.toml` way — reuses the
Turnstile widget already configured for `blog.koorevaar.com` (see
`ui-worker`'s own setup notes for where that secret lives), not a new widget
for this Worker.

`CACHE_INVALIDATION_SECRET` is also set the same off-`wrangler.toml` way —
pick any random value and configure the identical value as ACA's
`AssistantWorker:InvalidateCacheKey` (see the main API project's own
`appsettings.json`/environment config), since this is a shared secret both
sides must agree on, not one ACA reads back from here.

`SEARCH_SECRET` is the same shared-secret pattern again — pick any random
value and configure the identical value as `ui-worker`'s own `SEARCH_SECRET`
(`wrangler secret put SEARCH_SECRET` on both Workers).

## Deploy

```bash
npm run deploy
```
