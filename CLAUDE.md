# CLAUDE.md

Project orientation lives in [README.md](README.md) (architecture, stack, running locally, tests). This file covers conventions and cross-project context that aren't derivable from the code itself.

## Backlog

Cross-project backlog items are tracked on a private GitHub Projects (v2) board, **not** GitHub Issues in this repo: **"@P47K0's Backlog"**, project #1, owner `P47K0` (GraphQL node id `PVT_kwHOAGBE_c4Bh8HE`). Items are mostly draft issues with no backing repo, tagged by project (e.g. `blog`, `website`).

- `gh` is installed (for pushing branches), but query the board directly via GitHub's GraphQL API (`https://api.github.com/graphql`), using `ProjectV2` / `items(first: N) { nodes { fieldValues... content { ... on DraftIssue { title body } } } }`.
- Auth: `$BACKLOG_GITHUB_PAT` (fine-grained PAT, Projects read/write only) — see **Secrets** below.
- When the user references "the backlog" or "a backlog item" without pasting it, query this board rather than asking where it is.

## Secrets

`/workspace/.secrets/claude-tokens.env` holds this project's tokens (Azure AD app registrations for the API and KnowledgeBase, the backlog PAT, Cloudflare R2 keys, articles API key). It's gitignored, `chmod 600`, and auto-sourced into every shell via `BASH_ENV` — so its variables (e.g. `$BACKLOG_GITHUB_PAT`) are already available by name, no need to read or source the file yourself.

**Never echo a secret's value or cat this file's contents into a conversation.** Reference variables by name in scripts/commands and let the shell substitute them.

## Related repos

The personal site / homepage (blog.koorevaar.com's landing page, and the AI assistant widget's own frontend UI) is a **separate repo**: [github.com/P47K0/website](https://github.com/P47K0/website). Not part of this one. This repo covers the blog API, the comment-moderation Function, and the `api-proxy` / `ui-worker` / `assistant-worker` Cloudflare Workers only (the assistant's `/ask` and `/search` endpoints, not the widget UI that calls them). If a task touches the homepage or the widget's own UI, that's the repo to work in.

Static counters shown on the homepage (e.g. ACA/Function usage counters) are also maintained in that separate frontend project, not here.

## Git conventions

- Before starting new work, check whether the current branch is already merged into master; if so, branch fresh off master rather than continuing on a merged branch.
- After `git checkout -b <branch> origin/master`, verify the upstream tracking branch points at `<branch>` itself, not `origin/master`.

## Blog articles

- No em dashes or en dashes in article prose.
- New (and updated) articles do not get a "*Co-authored with Claude.*" byline in `content`: api-proxy adds it centrally (`ARTICLE_FOOTER`) under every listed article, so adding it would show it twice and embed it as its own KnowledgeBase chunk. Text specific to one article still goes at the end of its `content`.
- The same central footer also carries the Ko-fi line ("If this was useful, you can buy me a coffee", https://ko-fi.com/p47k0, since 2026-10-05). Don't add it to an article's `content` either.
- After uploading or updating an article, regenerate its KnowledgeBase embeddings with `python3 tools/embeddings/embed_article.py <slug>`. It embeds with bge-m3 through `tools/embeddings/embeddings.py`, the shared client for every batch caller: Cloudflare Workers AI first, the ACA bge-m3 server as fallback (quota error 4006, 429, 5xx, timeout). Neurons are shared with the visitor assistant, so it switches to the ACA when the account is within `NEURON_RESERVE` of the daily limit. Pass any `STALE` ids it prints to the user to delete by hand.
- Every new listed article gets a cover image: run `node tools/cover-images/make-cover.mjs --slug <slug>` with `--image <first inline image URL>`, or `--icon <Bootstrap Icons name matching the topic>` if the article has no image. Pass the printed URL as `coverImageUrl` in the `POST`. Skip unlisted articles.
- Every new listed article also gets `seoDescription` (at most 160 characters, written for search snippets, distinct from `summary`, no em or en dashes) and `seoKeywords` (4-7 search phrases people would actually type, distinct from `tags`, no commas inside an entry).
- Reuse existing tags: before choosing `tags` for a new article, list the tags in use (`GET /articles`, unique `tags`) and pick from them. Only add a new tag when no existing one fits and it will plausibly be used again. Canonical choices after the 2026-10-14 and 2026-10-15 vocabulary cleanups (do not reintroduce the left-hand variants): `certification` (not microsoft-certified, certification-journey, cloud-certification, ...), `ai` (not artificial-intelligence, generative-ai, ai-agents, agentic-*, multi-agent*, agent-framework, coding-agent), `ai-assisted-development` (not ai-coding), `local-llm` (not local-ai), `cloud-engineering` (not cloud-computing, cloud-skills), `cloud-architecture` (not cloud-architect), `azure-container-apps` (not aca, container-apps), `cloudflare-workers` (not workers), `azure-ai` (not microsoft-ai), `intel-arc`, `google-family-link`, `cost-optimization`, `debugging`, `career`, `qwen`, `claude` (not claude-code), `azure-foundry` (not azure-ai-foundry, foundry-local), `sql` (not d1). Removed on 2026-10-15, do not reintroduce: developer-life, ipex-llm, ux, workers-ai, nomic-embed-text. Exam tags stay as codes (`az-104`, `az-204`, ...). `blue-green-deployment`, `canary-deployment`, `deployment-strategies` and `aider` are deliberately separate.
- `PUT` is a full-record update: always send back the existing `coverImageUrl`, `seoDescription` and `seoKeywords`, or the update clears them.

## about.md

- `/about.md` is served from the homepage's `https://www.koorevaar.com/llms-full.txt` (website repo), with the Cosmos `about` article as fallback. The Cosmos article is also what the assistant's KnowledgeBase embeddings (`about-paragraph-{n}`, `about-summary`) are built from.
- After a homepage change that changes `llms-full.txt` is deployed, or when the user asks, run `tools/about-sync/sync_about.py` (usage in its docstring): it updates the Cosmos article and re-embeds with the shared `tools/embeddings` client, and does nothing when unchanged. Pass any `STALE` chunk ids it prints to the user to delete by hand.
- Manual for now. Moving it to a workflow that embeds with Cloudflare Workers AI is an open backlog item ("Generate about.md from the same source as the homepage").

## Workers AI model names

Model ids are spread over code, workflow variables and deployed settings, so after a Cloudflare deprecation notice check all of these (inventory of 2026-11-07, update it when something moves):

- Both the assistant-worker and the moderation Function moved to `@cf/google/gemma-4-26b-a4b-it` (branch feat/gemma-4-models, 2026-11-07; gemma-4 needs `chat_template_kwargs: {enable_thinking: false}`). The moderation default only reaches the live Function App when the infra workflow runs, check `Moderation__ModerationModelId` in Azure afterwards.
- assistant-worker: constant `GENERATION_MODEL` in `Pat.Aca.BlogServiceApi/Cloudflare/assistant-worker/src/lib/generation.ts`; embeddings (`bge-m3`) in `src/lib/embeddings.ts` and `tools/embeddings/embeddings.py`.
- Comment moderation Function: default in `ModerationSettings.cs` and bicep param `moderationModelId`. `comments-function-infra.yml` does not pass it, so the live value is the Function App setting `Moderation__ModerationModelId` in Azure. Confirmed live 2026-11-07: `llama-3.2-1b-instruct` (deprecated 2026-11-20). Gotcha: the setting is part of the bicep `appSettings`, so every run of the infra workflow resets it to the bicep default, wiping a value changed in the portal; change the bicep default (or pass the param) as well as the portal.
- Pat.Aca.HangmanApi (own repo): GitHub repo variable `CF_MODEL_ID`, deployed as `Cloudflare__ModelId` on the ACA by `docker-publish.yml`; the code default in `HangmanRunner.cs` is only a fallback.
- Website workers (website repo): hardcoded in `cloudflare/contact-worker.js` and `cloudflare/ollama-worker.js`. The repo files are copies pasted into Cloudflare and can lag the deployed workers: the deployed ollama-worker uses `llama-3.1-8b-instruct-fp8` (user, 2026-11-07) while the repo copy says `llama-3.1-8b-instruct`. The deployed contact-worker matches its repo copy (`llama-3.1-8b-instruct`, no -fp8, not deprecated; user, 2026-11-07). Check the deployed code in the Cloudflare dashboard.
- Capacity advisor worker (website repo `cloudflare/cap-advisor-demo.js`, probably): the deployed code uses `@cf/meta/llama-3.1-8b-instruct-fast` (user, 2026-11-07), which the repo copy does not show. Not on the 2026-11-20 deprecation list, but not in Cloudflare's docs or pricing table under that name either (only `-fp8-fast`); treat as an undocumented alias.
- Other ACAs: check their environment variables (Azure portal) for `Model`/`@cf/` values.
- Not in this workspace: the AKS text/sentiment-analysis demo.

Prices: https://developers.cloudflare.com/workers-ai/platform/pricing/index.md (plain curl works). The PAT cannot read GitHub repo variables (403), ask the user for those values.
