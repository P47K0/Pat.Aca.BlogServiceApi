import { marked } from 'marked';

export interface Env {
  /** Base URL of the deployed Pat.Aca.BlogServiceApi Container App. Plain
   * config (wrangler.toml [vars]), not a secret. */
  API_BASE_URL: string;
  /** Shared secret sent as X-Api-Key to the API's article endpoints. Set via
   * `wrangler secret put ARTICLES_API_KEY` — never checked into wrangler.toml. */
  ARTICLES_API_KEY: string;
  /** Durable (not per-colo, unlike the Cache API below) fallback store —
   * holds both the latest-10 article list snapshot (written either by this
   * Worker's own origin fetches, or by ArticleListSyncFunction's Change-Feed
   * push, see "Article list sync" below) and a per-slug snapshot of every
   * individual article ever fetched; see the fallback sections below. Also
   * holds the blog-post count under a third key (see "Article count"
   * below) and the most-viewed article under a fourth (see "Most-viewed
   * article" below) — same namespace, same "durable global store", just
   * different producers (ArticleCountSyncFunction/MostViewedSyncFunction/
   * ArticleListSyncFunction, not always this Worker itself). */
  ARTICLES_FALLBACK: KVNamespace;
  /** Shared secret POST /internal/article-count requires as
   * X-Article-Count-Sync-Key. Set via
   * `wrangler secret put ARTICLE_COUNT_SYNC_SECRET` — never checked into
   * wrangler.toml. Must match ArticleCountSyncFunction's own
   * ApiProxy__ArticleCountSyncSecret app setting. */
  ARTICLE_COUNT_SYNC_SECRET: string;
}

// Must match Pat.Aca.BlogServiceApi's ApiSecurity.ApiKeyHeaderName exactly.
const API_KEY_HEADER = 'X-Api-Key';

// Must match Pat.Aca.BlogServiceApi's ApiSecurity.RealClientIpHeaderName
// exactly. Set by ui-worker from the *original* incoming request's
// CF-Connecting-IP (accurate there — it's the real browser's edge
// connection) before its own outbound fetch() to this Worker, since
// Cloudflare doesn't carry the original CF-Connecting-IP across a
// Worker-to-Worker hop the way it does browser-to-edge. This Worker never
// re-derives its own CF-Connecting-IP for this purpose — it would just be
// ui-worker's own egress at that point, not the reader's — it only relays
// whatever ui-worker already resolved, onward to the API unchanged.
const REAL_CLIENT_IP_HEADER = 'X-Real-Client-Ip';

// ui-worker is the intended frontend, so this is scoped to its custom domain.
// Note this is NOT access control — CORS only governs whether a *browser*
// may read a cross-origin response; it does nothing against curl, another
// Worker's fetch(), or any other non-browser caller (and ui-worker's own
// calls to this Worker are server-side, so CORS doesn't even apply to them).
// Real restriction to ui-worker only — a shared-secret header or a Service
// Binding — is deliberately deferred, not implemented here yet.
//
// POST added alongside GET/OPTIONS specifically for the comment-submission
// route below — every other route stays GET-only, enforced in the fetch
// handler itself, not just by what CORS happens to allow.
const CORS_HEADERS: Record<string, string> = {
  'Access-Control-Allow-Origin': 'https://blog.koorevaar.com',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

// Moved up here (used well beyond the comments section further down) since
// both the durable-fallback code and the comments routes need to recognize
// an article detail path.
const ARTICLE_SLUG_PATH = /^\/articles\/[^/]+$/;
const ARTICLE_COMMENTS_PATH = /^\/articles\/[^/]+\/comments$/;

// --- Stale-while-revalidate cache -----------------------------------------
//
// blog-service-api's Container App scales to zero, so any request after an
// idle period pays full cold-start latency — worst case a reader clicking an
// article link right after the container's gone idle. Rather than a plain
// TTL cache (which still makes *someone* pay the cold start once the TTL
// expires), this always serves whatever is currently cached immediately, and
// only refreshes the cache from origin in the background, at most once per
// REVALIDATE_INTERVAL_MS per route. That interval is a rate-limit on
// re-checking the origin, not an expiry — a cached entry never goes stale
// from age alone, so no real visitor waits on a cold start once *any* cached
// copy exists. Only a true first-ever request for a route (or a cache entry
// evicted by Cloudflare) still pays a synchronous cold start.
//
// Accepted trade-off: the background refresh still hits the real origin and
// increments its viewCount for real, but only once per interval regardless
// of how many reads happen in between — an accepted undercount, not a bug.
// No purge-on-write is needed either: staleness after an edit the user makes
// themselves is already bounded to REVALIDATE_INTERVAL_MS by the background
// refresh itself.
//
// A cold start on this container was measured at ~30s (2026-09-04), well
// under this interval — so the interval isn't hiding an unknown worst case,
// it's a deliberate choice of how often to pay a background (never
// visitor-facing) cold start in exchange for fresher content and a tighter
// viewCount undercount bound after an edit.
const REVALIDATE_INTERVAL_MS = 1 * 60 * 1000;

// Custom header recording when a cached response was fetched from origin, so
// a later request can tell whether it's due for a background refresh. Not a
// standard cache-control header — we want "never auto-expire, just rate-limit
// re-checks", which s-maxage/Cache-Control don't express on their own.
const CACHED_AT_HEADER = 'X-Swr-Cached-At';

// Cloudflare's Cache API is per-colo, not a single global cache, so
// "at most once per ~3 min per route" is actually per edge location that
// happens to see traffic for that route — a known limitation of this
// approach, accepted rather than reaching for a global store (e.g. KV) for
// what's ultimately a soft rate-limit on re-checking origin.
const cache = caches.default;

/** Cache key. Includes the query string — needed since GET /articles?limit=
 * &after= (infinite-scroll pagination, below) is a genuinely different
 * response per combination of params, unlike every other route/request this
 * Worker has ever served, which never varied by query string. The plain
 * `/articles` and `/articles/{slug}` requests (home page, tag pages,
 * sitemap.xml, feed.xml) never carry a query string, so their cache key is
 * unchanged — this only starts mattering for the new paginated requests. */
function cacheKeyFor(pathname: string, search: string, requestUrl: string): Request {
  return new Request(new URL(pathname + search, requestUrl).toString(), { method: 'GET' });
}

// Shape returned by GET /articles and GET /articles/{slug} on the API side
// (System.Text.Json's Web defaults camelCase the Article record's properties).
interface Article {
  id: number;
  slug: string;
  title: string;
  summary: string;
  /** Raw Markdown coming in from the API; replaced with rendered HTML before
   * this Worker responds — the frontend never sees Markdown. */
  content: string;
  publishedAt: string;
  tags: string[];
  viewCount: number;
  linkedinVideoEmbedUrl?: string | null;
  coverImageUrl?: string | null;
  seoDescription?: string | null;
  seoKeywords?: string[] | null;
  /** Hides the article from lists; also means it gets no ARTICLE_FOOTER
   * (e.g. the `about` CV document the AI assistant reads). */
  unlisted?: boolean;
  /** Not stored by the API: set here to the rendered ARTICLE_FOOTER for
   * listed articles, so ui-worker has one place to take it from. */
  footer?: string | null;
}

/** The one footer shown under every listed article, in Markdown. It lives
 * here rather than in each article's `content`, where it would be embedded
 * into the KnowledgeBase as its own chunk on every article. Text specific
 * to one article belongs at the end of that article's `content`. */
const ARTICLE_FOOTER = '*Co-authored with Claude.*';

/** Every article's Markdown source conventionally opens with a `# Title`
 * line mirroring `article.title` — but ArticleDetailPage (ui-worker) already
 * renders its own `<h1>{article.title}</h1>` above the content, so that
 * leading heading renders a second time, styled differently by the `.prose`
 * typography classes. Strip it here, once, for every reader — cheaper than
 * editing 35+ stored articles individually.
 *
 * Only strips when the heading's text actually equals the title: at least
 * one article (`website-blog-feature`) opens with a genuinely different
 * subtitle rather than a repeated title, and that must survive untouched. */
function stripDuplicateLeadingH1(html: string, title: string): string {
  const match = html.match(/^\s*<h1[^>]*>([\s\S]*?)<\/h1>\s*/i);
  if (!match) return html;

  const headingText = match[1]
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .trim();

  return headingText === title.trim() ? html.slice(match[0].length) : html;
}

function renderArticleContent(article: Article): Article {
  return {
    ...article,
    content: stripDuplicateLeadingH1(
      marked.parse(article.content, { async: false }) as string,
      article.title,
    ),
    footer: article.unlisted ? null : (marked.parse(ARTICLE_FOOTER, { async: false }) as string),
  };
}

/** Result of fetching from origin: `cacheable` is true only for a
 * successfully rendered article JSON response — error/non-JSON passthroughs
 * are never cached, so a transient origin failure can't clobber a good cached
 * copy, and a stale-but-good entry just gets retried on the next request. */
interface UpstreamResult {
  response: Response;
  cacheable: boolean;
}

// Forwarded from the API's response as-is when present — the infinite-scroll
// "load more" pagination signal (see GET /articles?limit=&after= on the API
// side). Absent entirely on an unpaginated request, same as today.
const PAGINATION_HEADERS = ['X-Has-More', 'X-Next-Cursor'];

/** Fetches from the API, attaching the shared API key, and — for successful
 * JSON responses only — rewrites each article's `content` from Markdown to
 * HTML. Error responses (the API's RFC 7807 problem+json for 401/404/429/500)
 * are passed through untouched, nothing to render there. */
async function fetchAndRender(env: Env, pathname: string, search: string): Promise<UpstreamResult> {
  const upstreamUrl = new URL(pathname + search, env.API_BASE_URL);

  const upstreamResponse = await fetch(upstreamUrl.toString(), {
    method: 'GET',
    headers: {
      [API_KEY_HEADER]: env.ARTICLES_API_KEY,
      Accept: 'application/json',
    },
  });

  const contentType = upstreamResponse.headers.get('content-type') ?? '';
  if (!upstreamResponse.ok || !contentType.includes('application/json')) {
    const headers = new Headers(upstreamResponse.headers);
    for (const [key, value] of Object.entries(CORS_HEADERS)) {
      headers.set(key, value);
    }
    return {
      response: new Response(upstreamResponse.body, { status: upstreamResponse.status, headers }),
      cacheable: false,
    };
  }

  const body = await upstreamResponse.json();
  const rendered = Array.isArray(body)
    ? (body as Article[]).map(renderArticleContent)
    : renderArticleContent(body as Article);

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    [CACHED_AT_HEADER]: String(Date.now()),
    ...CORS_HEADERS,
  };
  for (const name of PAGINATION_HEADERS) {
    const value = upstreamResponse.headers.get(name);
    if (value !== null) headers[name] = value;
  }

  return {
    response: new Response(JSON.stringify(rendered), {
      status: upstreamResponse.status,
      headers,
    }),
    cacheable: true,
  };
}

/** Background refresh: refetches from origin and, only on success, replaces
 * the cache entry (updating its cached-at timestamp). On failure the
 * existing cached copy — stale or not — is left exactly as-is, so it keeps
 * being served and the next request past the interval just tries again. */
async function revalidate(env: Env, pathname: string, search: string, key: Request): Promise<void> {
  const { response, cacheable } = await fetchAndRender(env, pathname, search);
  if (cacheable) {
    // Snapshot from a clone *before* cache.put() gets the original — put()
    // consumes the response body, so cloning after would be too late. Only
    // the plain (unpaginated) list request updates the durable "latest 10"
    // snapshot — a paginated page's background refresh has no business
    // overwriting it with a partial slice. Article detail requests update
    // their own per-slug snapshot instead (see writeDetailFallbackSnapshot).
    if (pathname === LIST_PATH && search === '') {
      await writeFallbackSnapshot(env, response.clone());
    } else if (ARTICLE_SLUG_PATH.test(pathname)) {
      await writeDetailFallbackSnapshot(env, extractSlug(pathname), response.clone());
    }
    await cache.put(key, response);
  }
}

// --- Article count -----------------------------------------------------
//
// GET /articles/count backs the site's blog-post counter with a plain
// integer. Unlike every other article route, this is now a PURE KV READ
// with NO origin call at all -- not even the stale-while-revalidate
// racing-with-a-durable-fallback shape the list/detail routes use. The
// count in ARTICLES_FALLBACK is kept current by ArticleCountSyncFunction
// (Pat.Aca.BlogCommentsModerationFunction), a Cosmos DB Change Feed
// trigger on the Articles container that recomputes the count on every
// write and POSTs it to POST /internal/article-count below -- so there's
// structurally no cold-start dependency left on this read path at all,
// rather than one that's merely hidden/raced most of the time. This is
// what originally motivated this whole change: the list/detail SWR+KV-
// fallback design still lets a true first-ever request pay a synchronous
// cold start; a pure KV read never can.
//
// Deliberately checked before ARTICLE_SLUG_PATH in the dispatcher below --
// that regex would otherwise treat "count" as an article slug and route
// here into the wrong (article-shaped) machinery entirely.
const ARTICLES_COUNT_PATH = '/articles/count';
const ARTICLE_COUNT_KV_KEY = 'article-count';

async function readArticleCount(env: Env): Promise<Response> {
  const stored = await env.ARTICLES_FALLBACK.get(ARTICLE_COUNT_KV_KEY);
  // Missing only in the narrow window before ArticleCountSyncFunction has
  // ever run for the first time (e.g. right after this feature's first
  // deploy, before any article write has fired the Change Feed trigger
  // once) -- 0 is a safe, honest default for that window rather than
  // falling back to an origin call, which would reintroduce exactly the
  // cold-start dependency this design exists to remove. Self-heals on the
  // next real article write.
  const count = stored === null ? 0 : Number(stored);

  return new Response(JSON.stringify({ count }), {
    status: 200,
    headers: { ...CORS_HEADERS, 'Content-Type': 'application/json' },
  });
}

// POST /internal/article-count -- ArticleCountSyncFunction's own write
// path into ARTICLES_FALLBACK. Not a public route: never linked from the
// site, and gated on a shared secret (mirrors assistant-worker's own
// POST /internal/invalidate-cache pattern) rather than the public
// ARTICLES_API_KEY, since this is a completely different trust
// relationship (an Azure Function calling in, not a reader calling out).
// Fails closed if the secret is unset/empty or doesn't match, same
// posture as ApiSecurity.RequireApiKey in the sibling API project.
const ARTICLE_COUNT_SYNC_PATH = '/internal/article-count';
const ARTICLE_COUNT_SYNC_KEY_HEADER = 'X-Article-Count-Sync-Key';

async function handleArticleCountSync(request: Request, env: Env): Promise<Response> {
  const providedKey = request.headers.get(ARTICLE_COUNT_SYNC_KEY_HEADER);
  if (!env.ARTICLE_COUNT_SYNC_SECRET || providedKey !== env.ARTICLE_COUNT_SYNC_SECRET) {
    return new Response(null, { status: 401 });
  }

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return new Response(null, { status: 400 });
  }

  const count = (body as { count?: unknown } | null)?.count;
  if (typeof count !== 'number' || !Number.isInteger(count) || count < 0) {
    return new Response(null, { status: 400 });
  }

  await env.ARTICLES_FALLBACK.put(ARTICLE_COUNT_KV_KEY, String(count));
  return new Response(null, { status: 204 });
}

// --- Most-viewed article -------------------------------------------------
//
// Backs the homepage's "most-viewed post" link (see the backlog item of
// that name). Kept current by MostViewedSyncFunction, a twice-daily
// TimerTrigger -- NOT a Change-Feed reaction like article count, since
// ViewCount changes on every article read, far too often to react to
// per-write the way the count feature does. Reuses the article-count
// sync's own secret (ARTICLE_COUNT_SYNC_SECRET/X-Article-Count-Sync-Key)
// rather than a second one -- same trust relationship (this project's own
// Function calling in), see ApiProxySettings.ArticleCountSyncSecret's doc
// comment in the Function project for why.
const MOST_VIEWED_ARTICLE_PATH = '/articles/most-viewed';
const MOST_VIEWED_ARTICLE_KV_KEY = 'most-viewed-article';
// Comfortably longer than the twice-daily sync interval (12h) -- not the
// primary refresh mechanism (the TimerTrigger overwrites this well before
// it'd ever expire), just a safety net so a silently-broken sync
// eventually surfaces as "nothing to show" rather than serving
// indefinitely stale data forever.
const MOST_VIEWED_ARTICLE_KV_TTL_SECONDS = 60 * 60 * 48;

async function readMostViewedArticle(env: Env): Promise<Response> {
  const stored = await env.ARTICLES_FALLBACK.get(MOST_VIEWED_ARTICLE_KV_KEY);
  if (stored === null) {
    // Nothing synced yet (e.g. right after this feature's first deploy,
    // before MostViewedSyncFunction has run once) -- no fallback to
    // origin, same reasoning as readArticleCount above. ui-worker just
    // hides the widget on a 204.
    return new Response(null, { status: 204, headers: CORS_HEADERS });
  }

  return new Response(stored, {
    status: 200,
    headers: { ...CORS_HEADERS, 'Content-Type': 'application/json' },
  });
}

const MOST_VIEWED_ARTICLE_SYNC_PATH = '/internal/most-viewed-article';

async function handleMostViewedArticleSync(request: Request, env: Env): Promise<Response> {
  const providedKey = request.headers.get(ARTICLE_COUNT_SYNC_KEY_HEADER);
  if (!env.ARTICLE_COUNT_SYNC_SECRET || providedKey !== env.ARTICLE_COUNT_SYNC_SECRET) {
    return new Response(null, { status: 401 });
  }

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return new Response(null, { status: 400 });
  }

  const candidate = body as {
    slug?: unknown; title?: unknown; summary?: unknown; viewCount?: unknown; coverImageUrl?: unknown;
  } | null;
  if (
    typeof candidate?.slug !== 'string' || !candidate.slug ||
    typeof candidate.title !== 'string' || !candidate.title ||
    typeof candidate.summary !== 'string' ||
    typeof candidate.viewCount !== 'number' || !Number.isInteger(candidate.viewCount) || candidate.viewCount < 0 ||
    // Optional/nullable, like linkedinVideoEmbedUrl in the article list
    // sync. Already validated as an absolute https URL on the API's write
    // path, so only the type is re-checked here.
    (candidate.coverImageUrl !== undefined && candidate.coverImageUrl !== null &&
      typeof candidate.coverImageUrl !== 'string')
  ) {
    return new Response(null, { status: 400 });
  }

  await env.ARTICLES_FALLBACK.put(
    MOST_VIEWED_ARTICLE_KV_KEY,
    JSON.stringify({
      slug: candidate.slug,
      title: candidate.title,
      summary: candidate.summary,
      viewCount: candidate.viewCount,
      coverImageUrl: candidate.coverImageUrl ?? null,
    }),
    { expirationTtl: MOST_VIEWED_ARTICLE_KV_TTL_SECONDS },
  );
  return new Response(null, { status: 204 });
}

// --- Article list sync ---------------------------------------------------
//
// Backs the "Blog auto update: Cosmos DB Change Feed -> Azure Function ->
// Worker/KV" backlog item: ArticleListSyncFunction reacts to every write on
// the Articles container and pushes a freshly-recomputed latest-articles
// list here, so the durable snapshot (see "Durable list fallback" above)
// reflects a Cosmos write near-real-time instead of waiting on the next
// request-driven revalidate (up to REVALIDATE_INTERVAL_MS stale) or cold-miss
// to notice it. Writes into the exact same FALLBACK_KV_KEY snapshot the
// list route's own SWR/fallback machinery reads from and writes to —
// fresherFallbackResponse, readFallbackSnapshot, and every reader of that
// key need no changes at all: this is just a second producer of a shape
// that already exists, using the same writeFallbackSnapshotFromArticles
// sort/cap/dedupe/writtenAt logic as the request-driven writer.
//
// Reuses the article-count sync's own shared secret (same reasoning as
// most-viewed-article's sync) rather than a third one.
const ARTICLE_LIST_SYNC_PATH = '/internal/article-list';

/** Narrow, hand-rolled validation (same style as handleMostViewedArticleSync
 * above) rather than a schema library — this Worker has no such dependency
 * elsewhere, and the shape is small enough not to need one.
 * `linkedinVideoEmbedUrl` is the one optional/nullable field, matching the
 * Article interface's own `?: string | null`. `content` is deliberately not
 * part of the wire payload at all — ArticleCountSyncFunction's sibling,
 * ArticleListSyncFunction, never fetches article bodies from Cosmos for
 * this sync (see its own doc comment), so it's filled in as '' here,
 * matching what writeFallbackSnapshotFromArticles already blanks it to
 * anyway. */
function isValidSyncedArticle(candidate: unknown): candidate is Omit<Article, 'content'> {
  const article = candidate as Record<string, unknown> | null;
  return (
    typeof article?.id === 'number' && Number.isInteger(article.id) &&
    typeof article.slug === 'string' && article.slug.length > 0 &&
    typeof article.title === 'string' &&
    typeof article.summary === 'string' &&
    typeof article.publishedAt === 'string' &&
    Array.isArray(article.tags) && article.tags.every((tag) => typeof tag === 'string') &&
    typeof article.viewCount === 'number' && Number.isInteger(article.viewCount) && article.viewCount >= 0 &&
    (article.linkedinVideoEmbedUrl === undefined ||
      article.linkedinVideoEmbedUrl === null ||
      typeof article.linkedinVideoEmbedUrl === 'string')
  );
}

async function handleArticleListSync(request: Request, env: Env): Promise<Response> {
  const providedKey = request.headers.get(ARTICLE_COUNT_SYNC_KEY_HEADER);
  if (!env.ARTICLE_COUNT_SYNC_SECRET || providedKey !== env.ARTICLE_COUNT_SYNC_SECRET) {
    return new Response(null, { status: 401 });
  }

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return new Response(null, { status: 400 });
  }

  const candidateArticles = (body as { articles?: unknown } | null)?.articles;
  if (!Array.isArray(candidateArticles) || !candidateArticles.every(isValidSyncedArticle)) {
    return new Response(null, { status: 400 });
  }

  const articles: Article[] = candidateArticles.map((article) => ({ ...article, content: '' }));
  await writeFallbackSnapshotFromArticles(env, articles);
  return new Response(null, { status: 204 });
}

// --- Article markdown --------------------------------------------------
//
// GET /articles/{slug}.md and GET /about.md serve the raw Markdown `content`
// field 1:1 (no cleanup — decided when this feature was designed) instead of
// the rendered HTML every other route returns, for AI crawlers/agents and a
// human's own "view as Markdown" link. Deliberately NOT routed through
// fetchAndRender (which unconditionally renders `content` via marked.parse())
// -- this is the one place that skips that step on purpose. `/about.md` is
// just this same mechanism pointed at the fixed slug "about" -- the CV/about
// document is an ordinary Article (Unlisted so it never shows up in the
// list/sitemap/feed/tag cloud), not a separate resource. Gets the same
// same simple per-colo SWR treatment article count used to get (before it
// moved to a pure KV read, see "Article count" above), not the full
// durable-KV-fallback machinery the HTML detail route gets: this is a
// secondary, lower-traffic surface (crawlers, not the primary reader path),
// so a slow cold start here just means a slower crawl, not a broken page.
const ARTICLE_MD_PATH = /^\/articles\/([^/]+)\.md$/;
const ABOUT_MD_PATH = '/about.md';
const ABOUT_SLUG = 'about';

async function fetchArticleMarkdown(env: Env, slug: string): Promise<Response> {
  const upstreamUrl = new URL(`/articles/${encodeURIComponent(slug)}`, env.API_BASE_URL);
  const upstreamResponse = await fetch(upstreamUrl.toString(), {
    method: 'GET',
    headers: {
      [API_KEY_HEADER]: env.ARTICLES_API_KEY,
      Accept: 'application/json',
    },
  });

  const headers = new Headers();
  for (const [key, value] of Object.entries(CORS_HEADERS)) {
    headers.set(key, value);
  }

  if (!upstreamResponse.ok) {
    // Passes through the API's own RFC 7807 problem+json body/status (404
    // for an unknown/future-dated slug) rather than a bespoke error shape.
    headers.set('Content-Type', upstreamResponse.headers.get('content-type') ?? 'application/problem+json');
    return new Response(upstreamResponse.body, { status: upstreamResponse.status, headers });
  }

  const article = (await upstreamResponse.json()) as Article;
  headers.set('Content-Type', 'text/markdown; charset=utf-8');
  headers.set(CACHED_AT_HEADER, String(Date.now()));
  // Under a thematic break, the shape the byline had when it still lived
  // inside `content`.
  const markdown = article.unlisted ? article.content : `${article.content.trimEnd()}\n\n---\n\n${ARTICLE_FOOTER}\n`;
  return new Response(markdown, { status: 200, headers });
}

async function proxyArticleMarkdownRequest(
  env: Env,
  ctx: ExecutionContext,
  slug: string,
  key: Request,
): Promise<Response> {
  const cached = await cache.match(key);
  if (cached) {
    const cachedAt = Number(cached.headers.get(CACHED_AT_HEADER)) || 0;
    if (Date.now() - cachedAt > REVALIDATE_INTERVAL_MS) {
      ctx.waitUntil(
        fetchArticleMarkdown(env, slug).then((response) => (response.ok ? cache.put(key, response.clone()) : undefined)),
      );
    }
    return cached;
  }

  const response = await fetchArticleMarkdown(env, slug);
  if (response.ok) {
    ctx.waitUntil(cache.put(key, response.clone()));
  }
  return response;
}

// --- Durable list fallback --------------------------------------------------
//
// The Cache API above is per-colo and can be empty for a route even on a
// long-lived site — a colo that just hasn't seen traffic for it, or an entry
// Cloudflare evicted. On that cold miss, the code below used to fall straight
// through to a synchronous origin fetch with nothing to serve — fine for a
// merely slow cold start (~30s, measured), but the visitor pays every second
// of it. This durable, non-per-colo fallback (Cloudflare KV — global reads,
// no colo blind spots) exists specifically to cover that gap for the article
// LIST route only: whenever a full list fetch from origin succeeds, the 10
// newest articles' list metadata (no `content` — the list view never needs
// it, and there's no reason to multiply origin load fetching bodies nobody
// may click) get written here. If a live origin fetch for the list is slow
// past ORIGIN_TIMEOUT_MS or fails outright, this snapshot is served instead.
//
// Originally scoped to the list route only — the article detail route (GET
// /articles/{slug}) relied on the per-route Cache API SWR caching above being
// enough on its own. Extended to individual articles too as of the backlog
// item filed 2026-09-11 (see the "Durable detail fallback" section below):
// that per-colo caching isn't actually enough, because a colo can be cold for
// one *specific* article even when it's not brand-new and other colos already
// have it cached — happened in production to a heavily-edited article a
// reader clicked straight from the home page onto a colo that had simply
// never served that slug before.
const LIST_PATH = '/articles';
const FALLBACK_KV_KEY = 'latest-10';
const FALLBACK_SIZE = 10;
const ORIGIN_TIMEOUT_MS = 2000;

/** True when two articles are identical apart from `viewCount` -- which the
 * origin bumps on every fetch by design (see this file's top-of-file SWR
 * comment on the accepted undercount), so it alone would make every
 * revalidate look like a content change even when nothing a reader would
 * notice actually changed. Lets the write functions below skip a KV write --
 * eventually-consistent, per-write-limited, and offering no freshness benefit
 * when the stored bytes wouldn't change anyway -- for the common case where a
 * background revalidate just re-confirms already-cached content. */
function articleContentEquals(a: Article, b: Article): boolean {
  return (
    a.id === b.id &&
    a.slug === b.slug &&
    a.title === b.title &&
    a.summary === b.summary &&
    a.content === b.content &&
    a.footer === b.footer &&
    a.publishedAt === b.publishedAt &&
    a.linkedinVideoEmbedUrl === b.linkedinVideoEmbedUrl &&
    a.coverImageUrl === b.coverImageUrl &&
    a.seoDescription === b.seoDescription &&
    (a.seoKeywords ?? []).join('\n') === (b.seoKeywords ?? []).join('\n') &&
    a.tags.length === b.tags.length &&
    a.tags.every((tag, i) => tag === b.tags[i])
  );
}

/** Stored shape for FALLBACK_KV_KEY. `writtenAt` (this Worker's own clock,
 * matching CACHED_AT_HEADER's epoch-ms convention) lets a reader on a
 * different, stale-cached colo tell whether this durable snapshot -- global,
 * unlike the per-colo Cache API entry above -- already reflects a fresher
 * confirmed state than what it has locally, per fresherFallbackResponse
 * below. */
interface FallbackSnapshot {
  writtenAt: number;
  articles: Article[];
}

/** Parses a stored FALLBACK_KV_KEY value, tolerating the pre-writtenAt shape
 * (a bare Article[]) that's still sitting in KV until this Worker's next
 * successful write replaces it -- treated as maximally stale (writtenAt: 0)
 * so fresherFallbackResponse never prefers it over a colo's own cache, while
 * readFallbackSnapshot's genuine-fallback use still reads it unchanged. */
function parseFallbackSnapshot(raw: string): FallbackSnapshot {
  const parsed = JSON.parse(raw);
  return Array.isArray(parsed) ? { writtenAt: 0, articles: parsed as Article[] } : (parsed as FallbackSnapshot);
}

/** Shared by both writers of FALLBACK_KV_KEY: a successful origin list fetch
 * (writeFallbackSnapshot below) and ArticleListSyncFunction's Change-Feed-
 * driven push (handleArticleListSync, see "Article list sync" below) — same
 * sort/cap/dedupe/writtenAt logic regardless of which one is calling, so the
 * two producers can never disagree about what a "fresh enough to write"
 * snapshot looks like. */
async function writeFallbackSnapshotFromArticles(env: Env, articles: Article[]): Promise<void> {
  const latest = [...articles]
    .sort((a, b) => b.publishedAt.localeCompare(a.publishedAt))
    .slice(0, FALLBACK_SIZE)
    .map((article) => ({ ...article, content: '' }));

  const existing = await env.ARTICLES_FALLBACK.get(FALLBACK_KV_KEY);
  if (existing) {
    const existingSnapshot = parseFallbackSnapshot(existing);
    if (
      latest.length === existingSnapshot.articles.length &&
      latest.every((article, i) => articleContentEquals(article, existingSnapshot.articles[i]))
    ) {
      return;
    }
  }

  const snapshot: FallbackSnapshot = { writtenAt: Date.now(), articles: latest };
  await env.ARTICLES_FALLBACK.put(FALLBACK_KV_KEY, JSON.stringify(snapshot));
}

async function writeFallbackSnapshot(env: Env, listResponse: Response): Promise<void> {
  const articles = (await listResponse.json()) as Article[];
  await writeFallbackSnapshotFromArticles(env, articles);
}

async function readFallbackSnapshot(env: Env): Promise<Response | null> {
  const stored = await env.ARTICLES_FALLBACK.get(FALLBACK_KV_KEY);
  if (!stored) return null;
  const snapshot = parseFallbackSnapshot(stored);
  return new Response(JSON.stringify(snapshot.articles), {
    status: 200,
    headers: { 'Content-Type': 'application/json', ...CORS_HEADERS },
  });
}

/** Addresses the "api-proxy cache can serve a very stale article list"
 * backlog item (filed 2026-09-06): a colo's own per-colo Cache API entry only
 * ever gets rechecked against origin when a request actually lands on it past
 * REVALIDATE_INTERVAL_MS -- a colo nobody hits just sits stale indefinitely,
 * with nothing proactively refreshing it. This durable snapshot, by contrast,
 * gets refreshed (see writeFallbackSnapshot) by *any* colo's successful
 * revalidate or cold-miss fetch, so at real traffic levels it's almost always
 * fresher than one specific idle colo's own copy.
 *
 * Called only once a cache hit is already past REVALIDATE_INTERVAL_MS -- i.e.
 * exactly the case that used to just serve the (possibly long-)stale
 * per-colo copy while kicking a background revalidate for next time. If the
 * durable snapshot is newer than this colo's own cached-at, serve it instead,
 * immediately, rather than making this one request eat the full staleness gap
 * that accumulated while this colo had no traffic. Returns null (falls
 * through to the previous behavior) when there's nothing newer to offer --
 * cheap either way: one extra KV read, not a write. Content-blanked, same as
 * every other use of this snapshot -- accepted already since the list view
 * never needs it (see the "Durable list fallback" section above). */
async function fresherFallbackResponse(env: Env, cachedAt: number): Promise<Response | null> {
  const stored = await env.ARTICLES_FALLBACK.get(FALLBACK_KV_KEY);
  if (!stored) return null;

  const snapshot = parseFallbackSnapshot(stored);
  if (snapshot.writtenAt <= cachedAt) return null;

  return new Response(JSON.stringify(snapshot.articles), {
    status: 200,
    headers: {
      'Content-Type': 'application/json',
      [CACHED_AT_HEADER]: String(snapshot.writtenAt),
      ...CORS_HEADERS,
    },
  });
}

/** Cold-miss handling for the plain (unpaginated) list request only — never
 * called for a paginated `?limit=&after=` request (see proxyArticlesRequest):
 * the durable snapshot only ever holds the latest-10 unpaginated shape, so it
 * has nothing to offer a specific page/cursor. Infinite-scroll's "load more"
 * requests fall through to the plain cache/fetch path below instead, with no
 * durable-fallback protection — an accepted gap, since that's a progressive
 * enhancement on top of an already-rendered page, not the critical first
 * paint this fallback exists to protect.
 *
 * Races the real origin fetch against ORIGIN_TIMEOUT_MS. If origin wins,
 * behaves exactly like the normal path (render, cache, snapshot). If the
 * timeout wins — or the fetch throws — and a durable snapshot exists, that's
 * served immediately while the real fetch keeps running in the background
 * (still updating the Cache API entry and the snapshot whenever it does
 * eventually resolve). If there's no snapshot yet (e.g. very first request
 * ever), this just falls back to waiting on origin, same as before this
 * feature existed. */
async function fetchListWithFallback(
  env: Env,
  ctx: ExecutionContext,
  key: Request,
): Promise<Response> {
  const originPromise = fetchAndRender(env, LIST_PATH, '').then(async ({ response, cacheable }) => {
    if (cacheable) {
      // Snapshot from clones taken while the body is still unread — both put()
      // and the eventual `return response` each need their own intact copy.
      await cache.put(key, response.clone());
      await writeFallbackSnapshot(env, response.clone());
    }
    return response;
  });

  // Never rejects: a thrown fetch (network error, DNS failure, timeout at the
  // platform level, etc.) becomes an 'unavailable' outcome exactly like a slow
  // origin hitting ORIGIN_TIMEOUT_MS, so both funnel into the same fallback
  // path below instead of throwing out of Promise.race.
  type Outcome = { kind: 'origin'; response: Response } | { kind: 'unavailable' };
  const settled: Promise<Outcome> = originPromise
    .then((response): Outcome => ({ kind: 'origin', response }))
    .catch((): Outcome => ({ kind: 'unavailable' }));
  const timedOut: Promise<Outcome> = new Promise((resolve) => {
    setTimeout(() => resolve({ kind: 'unavailable' }), ORIGIN_TIMEOUT_MS);
  });

  const winner = await Promise.race([settled, timedOut]);
  if (winner.kind === 'origin') {
    return winner.response;
  }

  // Origin is slower than we're willing to make a visitor wait for, or it
  // failed outright — let it keep running in the background so the cache and
  // snapshot still get updated if/when it does resolve, but don't block this
  // response on it.
  ctx.waitUntil(originPromise.then(() => undefined).catch(() => undefined));

  const fallback = await readFallbackSnapshot(env);
  // No snapshot yet (e.g. the very first request ever) — nothing to fall back
  // to, so wait on origin after all, same as before this feature existed.
  return fallback ?? originPromise;
}

// --- Durable detail fallback -------------------------------------------
//
// Same mechanism as the list fallback above, extended to individual articles
// per the backlog item filed 2026-09-11 (see the LIST_PATH comment above for
// the production incident that motivated it). Key differences from the list
// snapshot: every article gets its own KV entry — no cap, no cutoff to pick,
// since (unlike the list) there's no natural "latest N" that covers the
// actual failure mode — written on every successful origin fetch (cold-miss
// or background revalidate) whose content actually differs from what's
// already stored (see articleContentEquals — `viewCount` alone, which the
// origin bumps on every fetch, doesn't count as a change); and the full
// rendered `content` is kept, not blanked, since that's the entire point of
// this route. Reuses the same ARTICLES_FALLBACK namespace as the list
// snapshot rather than provisioning a second KV namespace for what's the
// same fallback purpose — a `article:{slug}` key prefix keeps the two from
// ever colliding. Storage stays tiny at this blog's scale (one entry per
// article ever fetched, a few KB each); write volume is bounded by real
// content changes, not by traffic — a background revalidate that re-fetches
// unchanged content (the common case) costs one extra KV read, not a write.
function detailFallbackKey(slug: string): string {
  return `article:${slug}`;
}

/** Extracts the slug from a pathname already matched against
 * ARTICLE_SLUG_PATH by the caller — a plain substring slice past the fixed
 * '/articles/' prefix, not a route param, since this Worker has no router. */
function extractSlug(pathname: string): string {
  return pathname.slice(LIST_PATH.length + 1);
}

async function writeDetailFallbackSnapshot(env: Env, slug: string, articleResponse: Response): Promise<void> {
  const article = (await articleResponse.json()) as Article;

  const existing = await env.ARTICLES_FALLBACK.get(detailFallbackKey(slug));
  if (existing && articleContentEquals(JSON.parse(existing) as Article, article)) {
    return;
  }

  await env.ARTICLES_FALLBACK.put(detailFallbackKey(slug), JSON.stringify(article));
}

async function readDetailFallbackSnapshot(env: Env, slug: string): Promise<Response | null> {
  const stored = await env.ARTICLES_FALLBACK.get(detailFallbackKey(slug));
  if (!stored) return null;
  return new Response(stored, {
    status: 200,
    headers: { 'Content-Type': 'application/json', ...CORS_HEADERS },
  });
}

/** Cold-miss handling for GET /articles/{slug} — mirrors fetchListWithFallback
 * exactly (same race against ORIGIN_TIMEOUT_MS, same background continuation
 * via ctx.waitUntil on a timeout/failure, same "no snapshot yet → wait on
 * origin" bottom-out); see that function's doc comment for the full
 * mechanism. The only difference is the fallback key is per-slug here rather
 * than the single fixed list key. */
async function fetchDetailWithFallback(
  env: Env,
  ctx: ExecutionContext,
  pathname: string,
  key: Request,
  slug: string,
): Promise<Response> {
  const originPromise = fetchAndRender(env, pathname, '').then(async ({ response, cacheable }) => {
    if (cacheable) {
      await cache.put(key, response.clone());
      await writeDetailFallbackSnapshot(env, slug, response.clone());
    }
    return response;
  });

  type Outcome = { kind: 'origin'; response: Response } | { kind: 'unavailable' };
  const settled: Promise<Outcome> = originPromise
    .then((response): Outcome => ({ kind: 'origin', response }))
    .catch((): Outcome => ({ kind: 'unavailable' }));
  const timedOut: Promise<Outcome> = new Promise((resolve) => {
    setTimeout(() => resolve({ kind: 'unavailable' }), ORIGIN_TIMEOUT_MS);
  });

  const winner = await Promise.race([settled, timedOut]);
  if (winner.kind === 'origin') {
    return winner.response;
  }

  ctx.waitUntil(originPromise.then(() => undefined).catch(() => undefined));

  const fallback = await readDetailFallbackSnapshot(env, slug);
  return fallback ?? originPromise;
}

/** Proxies a GET to the API with stale-while-revalidate caching: an existing
 * cache entry is always served immediately; a background refresh is kicked
 * off (not awaited) only when it's older than REVALIDATE_INTERVAL_MS. On a
 * cold cache miss, fetches synchronously (nothing to serve yet) and seeds
 * the cache for next time — except the *plain* list route (no query string)
 * and any article detail route, both of which race that fetch against a
 * durable KV fallback instead (see fetchListWithFallback/
 * fetchDetailWithFallback above). A paginated list request
 * (`/articles?limit=&after=`, infinite scroll's "load more") gets its own
 * cache entry via the query-string-aware cache key, but no durable fallback —
 * it's progressive enhancement on top of an already-rendered page, not the
 * critical first paint the fallback exists to protect.
 *
 * For the plain list route specifically, a past-interval hit first checks
 * fresherFallbackResponse before falling back to serving its own stale copy —
 * see that function's doc comment for why (the "stale article list" backlog
 * item: a background revalidate alone only helps colos that actually receive
 * traffic). */
async function proxyArticlesRequest(
  env: Env,
  ctx: ExecutionContext,
  pathname: string,
  search: string,
  requestUrl: string,
): Promise<Response> {
  const key = cacheKeyFor(pathname, search, requestUrl);
  const isPlainList = pathname === LIST_PATH && search === '';

  const cached = await cache.match(key);
  if (cached) {
    const cachedAt = Number(cached.headers.get(CACHED_AT_HEADER)) || 0;
    if (Date.now() - cachedAt > REVALIDATE_INTERVAL_MS) {
      if (isPlainList) {
        const fresher = await fresherFallbackResponse(env, cachedAt);
        if (fresher) {
          ctx.waitUntil(cache.put(key, fresher.clone()));
          ctx.waitUntil(revalidate(env, pathname, search, key));
          return fresher;
        }
      }
      ctx.waitUntil(revalidate(env, pathname, search, key));
    }
    return cached;
  }

  if (isPlainList) {
    return fetchListWithFallback(env, ctx, key);
  }

  if (ARTICLE_SLUG_PATH.test(pathname)) {
    return fetchDetailWithFallback(env, ctx, pathname, key, extractSlug(pathname));
  }

  const { response, cacheable } = await fetchAndRender(env, pathname, search);
  if (cacheable) {
    ctx.waitUntil(cache.put(key, response.clone()));
  }
  return response;
}

// --- Comments --------------------------------------------------------------
//
// Unlike article reads, comments are never cached (SWR or otherwise) — a
// reader's own freshly-submitted comment showing up promptly matters more
// here than shaving an origin round trip off a low-traffic, highly dynamic
// resource, and there's no cold-start-hiding motivation the way there was
// for articles (comments aren't the thing a reader clicks straight from a
// LinkedIn link). Both directions attach the shared ARTICLES_API_KEY, same
// as every article request.

/** GET /articles/{slug}/comments passthrough — no Markdown rendering (unlike
 * articles' `content`, a comment's `text` is always plain text), so this
 * just forwards the API's JSON response body/status as-is. */
async function proxyCommentsGet(env: Env, pathname: string): Promise<Response> {
  const upstreamUrl = new URL(pathname, env.API_BASE_URL);
  const upstreamResponse = await fetch(upstreamUrl.toString(), {
    method: 'GET',
    headers: {
      [API_KEY_HEADER]: env.ARTICLES_API_KEY,
      Accept: 'application/json',
    },
  });

  const headers = new Headers(upstreamResponse.headers);
  for (const [key, value] of Object.entries(CORS_HEADERS)) {
    headers.set(key, value);
  }
  return new Response(upstreamResponse.body, { status: upstreamResponse.status, headers });
}

/** POST /articles/{slug}/comments passthrough — relays the real visitor IP
 * (already resolved by ui-worker from the original request's
 * CF-Connecting-IP, see REAL_CLIENT_IP_HEADER above) onward to the API
 * unchanged, alongside the shared API key. Body is forwarded verbatim —
 * validation (required fields, length caps) is the API's job, not this
 * Worker's; this is purely a pass-through with the right headers attached. */
async function proxyCommentsPost(env: Env, request: Request, pathname: string): Promise<Response> {
  const upstreamUrl = new URL(pathname, env.API_BASE_URL);
  const realClientIp = request.headers.get(REAL_CLIENT_IP_HEADER) ?? '';

  const upstreamResponse = await fetch(upstreamUrl.toString(), {
    method: 'POST',
    headers: {
      [API_KEY_HEADER]: env.ARTICLES_API_KEY,
      [REAL_CLIENT_IP_HEADER]: realClientIp,
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: await request.text(),
  });

  const headers = new Headers(upstreamResponse.headers);
  for (const [key, value] of Object.entries(CORS_HEADERS)) {
    headers.set(key, value);
  }
  return new Response(upstreamResponse.body, { status: upstreamResponse.status, headers });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    if (request.method === 'OPTIONS') {
      return new Response(null, { headers: CORS_HEADERS });
    }

    const { pathname, search } = new URL(request.url);

    // Internal, not public — never reached via CORS/a browser, just
    // ArticleCountSyncFunction's own server-to-server call. Checked ahead
    // of the generic method gate below since it's POST but isn't the
    // reader-facing comments route.
    if (request.method === 'POST' && pathname === ARTICLE_COUNT_SYNC_PATH) {
      return handleArticleCountSync(request, env);
    }

    // Same internal-only reasoning as above -- MostViewedSyncFunction's own
    // server-to-server call.
    if (request.method === 'POST' && pathname === MOST_VIEWED_ARTICLE_SYNC_PATH) {
      return handleMostViewedArticleSync(request, env);
    }

    // Same internal-only reasoning as above -- ArticleListSyncFunction's
    // own server-to-server call.
    if (request.method === 'POST' && pathname === ARTICLE_LIST_SYNC_PATH) {
      return handleArticleListSync(request, env);
    }

    // The one *public* non-GET route this Worker supports — everything
    // else reader-facing stays GET-only, enforced below, not just by what
    // CORS happens to allow.
    if (request.method === 'POST' && ARTICLE_COMMENTS_PATH.test(pathname)) {
      return proxyCommentsPost(env, request, pathname);
    }

    if (request.method !== 'GET') {
      return new Response(null, { status: 405, headers: CORS_HEADERS });
    }

    if (ARTICLE_COMMENTS_PATH.test(pathname)) {
      return proxyCommentsGet(env, pathname);
    }

    // Checked before the generic /articles dispatch below -- ARTICLE_SLUG_PATH
    // would otherwise match this path too and treat "count" as an article slug.
    if (pathname === ARTICLES_COUNT_PATH) {
      return readArticleCount(env);
    }

    // Same reasoning -- ARTICLE_SLUG_PATH would otherwise treat
    // "most-viewed" as an article slug.
    if (pathname === MOST_VIEWED_ARTICLE_PATH) {
      return readMostViewedArticle(env);
    }

    // Also checked before the generic dispatch -- these serve raw Markdown,
    // never the rendered-HTML shape fetchAndRender produces.
    if (pathname === ABOUT_MD_PATH) {
      return proxyArticleMarkdownRequest(env, ctx, ABOUT_SLUG, cacheKeyFor(pathname, search, request.url));
    }
    const mdMatch = pathname.match(ARTICLE_MD_PATH);
    if (mdMatch) {
      return proxyArticleMarkdownRequest(env, ctx, mdMatch[1], cacheKeyFor(pathname, search, request.url));
    }

    // Routes mirror the API's exactly: /articles and /articles/{slug}. Only
    // /articles ever carries a query string (?limit=&after=, infinite
    // scroll's pagination) — the API ignores/doesn't expect one on the
    // detail route, so it's passed through here regardless without a
    // special case.
    if (pathname === '/articles' || ARTICLE_SLUG_PATH.test(pathname)) {
      return proxyArticlesRequest(env, ctx, pathname, search, request.url);
    }

    return new Response('Not found', { status: 404, headers: CORS_HEADERS });
  },
};
