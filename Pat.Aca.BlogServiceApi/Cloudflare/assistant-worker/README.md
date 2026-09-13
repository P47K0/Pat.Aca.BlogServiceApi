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

## Deploy

```bash
npm run deploy
```
