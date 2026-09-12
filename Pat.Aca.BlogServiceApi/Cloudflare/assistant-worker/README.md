# blog-assistant-worker

Cloudflare Worker behind the "AI chat/assistant that answers questions about
Patrick" feature. Sibling to `api-proxy` and `ui-worker` under `Cloudflare/`,
built the same way (its own `package.json`/`wrangler.toml`, no shared code).

Full design (RAG architecture, semantic cache, guardrails) lives in the
project's backlog item, not duplicated here. This is currently just a scaffold
(`GET /healthz`) — built up in small, separately reviewable commits per the
project's own conventions:

1. Scaffold.
2. Workers AI + KV bindings (this commit).
3. Semantic-cache lookup (embed the question, compare against cached results).
4. Cache-miss path: query the `KnowledgeBase` Cosmos container, populate the
   cache.
5. Prompt assembly with scoping guardrails.
6. Generation call via Workers AI, behind a response-shape adapter.
7. Return JSON — no UI yet (the chat widget itself is a later phase).

## Setup

```bash
npm install
wrangler login                                    # one-time, not done in this sandbox
wrangler kv namespace create ASSISTANT_CACHE      # then paste the real id into
                                                   # wrangler.toml's ASSISTANT_CACHE
                                                   # binding, replacing the placeholder
```

## Deploy

```bash
npm run deploy
```
