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
- After uploading or updating an article, regenerate its KnowledgeBase embeddings via the local Ollama `bge-m3` model.
- Every new listed article gets a cover image: run `node tools/cover-images/make-cover.mjs --slug <slug>` with `--image <first inline image URL>`, or `--icon <Bootstrap Icons name matching the topic>` if the article has no image. Pass the printed URL as `coverImageUrl` in the `POST`. Skip unlisted articles.
- Every new listed article also gets `seoDescription` (at most 160 characters, written for search snippets, distinct from `summary`, no em or en dashes) and `seoKeywords` (4-7 search phrases people would actually type, distinct from `tags`, no commas inside an entry).
- Reuse existing tags: before choosing `tags` for a new article, list the tags in use (`GET /articles`, unique `tags`) and pick from them. Only add a new tag when no existing one fits and it will plausibly be used again. Canonical choices after the 2026-10-14 and 2026-10-15 vocabulary cleanups (do not reintroduce the left-hand variants): `certification` (not microsoft-certified, certification-journey, cloud-certification, ...), `ai` (not artificial-intelligence, generative-ai, ai-agents, agentic-*, multi-agent*, agent-framework, coding-agent), `ai-assisted-development` (not ai-coding), `local-llm` (not local-ai), `cloud-engineering` (not cloud-computing, cloud-skills), `cloud-architecture` (not cloud-architect), `azure-container-apps` (not aca, container-apps), `cloudflare-workers` (not workers), `azure-ai` (not microsoft-ai), `intel-arc`, `google-family-link`, `cost-optimization`, `debugging`, `career`, `qwen`, `claude` (not claude-code), `azure-foundry` (not azure-ai-foundry, foundry-local), `sql` (not d1). Removed on 2026-10-15, do not reintroduce: developer-life, ipex-llm, ux, workers-ai, nomic-embed-text. Exam tags stay as codes (`az-104`, `az-204`, ...). `blue-green-deployment`, `canary-deployment`, `deployment-strategies` and `aider` are deliberately separate.
- `PUT` is a full-record update: always send back the existing `coverImageUrl`, `seoDescription` and `seoKeywords`, or the update clears them.

## about.md

- `/about.md` is served from the homepage's `https://www.koorevaar.com/llms-full.txt` (website repo), with the Cosmos `about` article as fallback. The Cosmos article is also what the assistant's KnowledgeBase embeddings (`about-paragraph-{n}`, `about-summary`) are built from.
- After a homepage change that changes `llms-full.txt` is deployed, or when the user asks, run `tools/about-sync/sync_about.py` (usage in its docstring): it updates the Cosmos article and re-embeds with local Ollama `bge-m3`, and does nothing when unchanged. Pass any `STALE` chunk ids it prints to the user to delete by hand.
- Manual for now. Moving it to a workflow that embeds with Cloudflare Workers AI is an open backlog item ("Generate about.md from the same source as the homepage").
