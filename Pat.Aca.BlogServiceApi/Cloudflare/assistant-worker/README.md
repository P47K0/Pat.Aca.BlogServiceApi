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

## Setup

```bash
npm install
wrangler login                                    # one-time, not done in this sandbox
wrangler kv namespace create ASSISTANT_CACHE      # then paste the real id into
                                                   # wrangler.toml's ASSISTANT_CACHE
                                                   # binding, replacing the placeholder
wrangler secret put KNOWLEDGEBASE_READER_CLIENT_SECRET
```

`KNOWLEDGEBASE_READER_TENANT_ID`/`_CLIENT_ID` in `wrangler.toml` are also
placeholders until the `KnowledgeBase-Reader` app registration exists (create
it by hand in the personal Entra tenant, same as `KnowledgeBase-Writer` — see
`infra/cosmos-db.bicep`'s `knowledgeBaseReaderPrincipalId`) and
`cosmos-db.yml` has been re-run with its principal ID.

## Deploy

```bash
npm run deploy
```
