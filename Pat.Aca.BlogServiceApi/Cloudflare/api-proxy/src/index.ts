import { marked } from 'marked';

export interface Env {
  /** Base URL of the deployed Pat.Aca.BlogServiceApi Container App. Plain
   * config (wrangler.toml [vars]), not a secret. */
  API_BASE_URL: string;
  /** Shared secret sent as X-Api-Key to the API's article endpoints. Set via
   * `wrangler secret put ARTICLES_API_KEY` — never checked into wrangler.toml. */
  ARTICLES_API_KEY: string;
  /** Durable (not per-colo, unlike the Cache API below) fallback store for
   * just the latest-10 article list — see the fallback section below. */
  ARTICLES_FALLBACK: KVNamespace;
}

// Must match Pat.Aca.BlogServiceApi's ApiSecurity.ApiKeyHeaderName exactly.
const API_KEY_HEADER = 'X-Api-Key';

// ui-worker is the intended frontend, so this is scoped to its custom domain.
// Note this is NOT access control — CORS only governs whether a *browser*
// may read a cross-origin response; it does nothing against curl, another
// Worker's fetch(), or any other non-browser caller (and ui-worker's own
// calls to this Worker are server-side, so CORS doesn't even apply to them).
// Real restriction to ui-worker only — a shared-secret header or a Service
// Binding — is deliberately deferred, not implemented here yet.
const CORS_HEADERS: Record<string, string> = {
  'Access-Control-Allow-Origin': 'https://blog.koorevaar.com',
  'Access-Control-Allow-Methods': 'GET, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

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
}

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
    // overwriting it with a partial slice.
    if (pathname === LIST_PATH && search === '') {
      await writeFallbackSnapshot(env, response.clone());
    }
    await cache.put(key, response);
  }
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
// Deliberately NOT extended to the article detail route (GET /articles/{slug})
// — the existing per-route Cache API SWR caching there already does exactly
// what's wanted (serve-if-cached, background-refresh, synchronous-fetch-on-
// true-cold-miss); opening an article that isn't already cached from a real
// prior visit still waits on origin, unchanged, by design.
const LIST_PATH = '/articles';
const FALLBACK_KV_KEY = 'latest-10';
const FALLBACK_SIZE = 10;
const ORIGIN_TIMEOUT_MS = 2000;

async function writeFallbackSnapshot(env: Env, listResponse: Response): Promise<void> {
  const articles = (await listResponse.json()) as Article[];
  const latest = [...articles]
    .sort((a, b) => b.publishedAt.localeCompare(a.publishedAt))
    .slice(0, FALLBACK_SIZE)
    .map((article) => ({ ...article, content: '' }));
  await env.ARTICLES_FALLBACK.put(FALLBACK_KV_KEY, JSON.stringify(latest));
}

async function readFallbackSnapshot(env: Env): Promise<Response | null> {
  const stored = await env.ARTICLES_FALLBACK.get(FALLBACK_KV_KEY);
  if (!stored) return null;
  return new Response(stored, {
    status: 200,
    headers: { 'Content-Type': 'application/json', ...CORS_HEADERS },
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

/** Proxies a GET to the API with stale-while-revalidate caching: an existing
 * cache entry is always served immediately; a background refresh is kicked
 * off (not awaited) only when it's older than REVALIDATE_INTERVAL_MS. On a
 * cold cache miss, fetches synchronously (nothing to serve yet) and seeds
 * the cache for next time — except the *plain* list route (no query string),
 * which races that fetch against a durable fallback (see
 * fetchListWithFallback above). A paginated list request
 * (`/articles?limit=&after=`, infinite scroll's "load more") gets its own
 * cache entry via the query-string-aware cache key, but not the durable
 * fallback — same generic cold-miss path as the article detail route. */
async function proxyArticlesRequest(
  env: Env,
  ctx: ExecutionContext,
  pathname: string,
  search: string,
  requestUrl: string,
): Promise<Response> {
  const key = cacheKeyFor(pathname, search, requestUrl);

  const cached = await cache.match(key);
  if (cached) {
    const cachedAt = Number(cached.headers.get(CACHED_AT_HEADER)) || 0;
    if (Date.now() - cachedAt > REVALIDATE_INTERVAL_MS) {
      ctx.waitUntil(revalidate(env, pathname, search, key));
    }
    return cached;
  }

  if (pathname === LIST_PATH && search === '') {
    return fetchListWithFallback(env, ctx, key);
  }

  const { response, cacheable } = await fetchAndRender(env, pathname, search);
  if (cacheable) {
    ctx.waitUntil(cache.put(key, response.clone()));
  }
  return response;
}

const ARTICLE_SLUG_PATH = /^\/articles\/[^/]+$/;

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    if (request.method === 'OPTIONS') {
      return new Response(null, { headers: CORS_HEADERS });
    }

    if (request.method !== 'GET') {
      return new Response(null, { status: 405, headers: CORS_HEADERS });
    }

    const { pathname, search } = new URL(request.url);

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
