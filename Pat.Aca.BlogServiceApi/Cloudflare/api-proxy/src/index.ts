import { marked } from 'marked';

export interface Env {
  /** Base URL of the deployed Pat.Aca.BlogServiceApi Container App. Plain
   * config (wrangler.toml [vars]), not a secret. */
  API_BASE_URL: string;
  /** Shared secret sent as X-Api-Key to the API's article endpoints. Set via
   * `wrangler secret put ARTICLES_API_KEY` — never checked into wrangler.toml. */
  ARTICLES_API_KEY: string;
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

/** Cache key independent of query string — this Worker's routing already
 * ignores query params, so the cache should too. */
function cacheKeyFor(pathname: string, requestUrl: string): Request {
  return new Request(new URL(pathname, requestUrl).toString(), { method: 'GET' });
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
}

function renderArticleContent(article: Article): Article {
  return {
    ...article,
    content: marked.parse(article.content, { async: false }) as string,
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

/** Fetches from the API, attaching the shared API key, and — for successful
 * JSON responses only — rewrites each article's `content` from Markdown to
 * HTML. Error responses (the API's RFC 7807 problem+json for 401/404/429/500)
 * are passed through untouched, nothing to render there. */
async function fetchAndRender(env: Env, pathname: string): Promise<UpstreamResult> {
  const upstreamUrl = new URL(pathname, env.API_BASE_URL);

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

  return {
    response: new Response(JSON.stringify(rendered), {
      status: upstreamResponse.status,
      headers: {
        'Content-Type': 'application/json',
        [CACHED_AT_HEADER]: String(Date.now()),
        ...CORS_HEADERS,
      },
    }),
    cacheable: true,
  };
}

/** Background refresh: refetches from origin and, only on success, replaces
 * the cache entry (updating its cached-at timestamp). On failure the
 * existing cached copy — stale or not — is left exactly as-is, so it keeps
 * being served and the next request past the interval just tries again. */
async function revalidate(env: Env, pathname: string, key: Request): Promise<void> {
  const { response, cacheable } = await fetchAndRender(env, pathname);
  if (cacheable) {
    await cache.put(key, response);
  }
}

/** Proxies a GET to the API with stale-while-revalidate caching: an existing
 * cache entry is always served immediately; a background refresh is kicked
 * off (not awaited) only when it's older than REVALIDATE_INTERVAL_MS. On a
 * cold cache miss, fetches synchronously (nothing to serve yet) and seeds
 * the cache for next time. */
async function proxyArticlesRequest(
  env: Env,
  ctx: ExecutionContext,
  pathname: string,
  requestUrl: string,
): Promise<Response> {
  const key = cacheKeyFor(pathname, requestUrl);

  const cached = await cache.match(key);
  if (cached) {
    const cachedAt = Number(cached.headers.get(CACHED_AT_HEADER)) || 0;
    if (Date.now() - cachedAt > REVALIDATE_INTERVAL_MS) {
      ctx.waitUntil(revalidate(env, pathname, key));
    }
    return cached;
  }

  const { response, cacheable } = await fetchAndRender(env, pathname);
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

    const { pathname } = new URL(request.url);

    // Routes mirror the API's exactly: /articles and /articles/{slug}.
    if (pathname === '/articles' || ARTICLE_SLUG_PATH.test(pathname)) {
      return proxyArticlesRequest(env, ctx, pathname, request.url);
    }

    return new Response('Not found', { status: 404, headers: CORS_HEADERS });
  },
};
