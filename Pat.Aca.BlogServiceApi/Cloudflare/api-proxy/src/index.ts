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
}

function renderArticleContent(article: Article): Article {
  return {
    ...article,
    content: marked.parse(article.content, { async: false }) as string,
  };
}

/** Proxies a GET to the API, attaching the shared API key, and — for
 * successful JSON responses only — rewrites each article's `content` from
 * Markdown to HTML. Error responses (the API's RFC 7807 problem+json for
 * 401/404/429/500) are passed through untouched, nothing to render there. */
async function proxyArticlesRequest(env: Env, pathname: string): Promise<Response> {
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
    return new Response(upstreamResponse.body, { status: upstreamResponse.status, headers });
  }

  const body = await upstreamResponse.json();
  const rendered = Array.isArray(body)
    ? (body as Article[]).map(renderArticleContent)
    : renderArticleContent(body as Article);

  return new Response(JSON.stringify(rendered), {
    status: upstreamResponse.status,
    headers: {
      'Content-Type': 'application/json',
      ...CORS_HEADERS,
    },
  });
}

const ARTICLE_SLUG_PATH = /^\/articles\/[^/]+$/;

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === 'OPTIONS') {
      return new Response(null, { headers: CORS_HEADERS });
    }

    if (request.method !== 'GET') {
      return new Response(null, { status: 405, headers: CORS_HEADERS });
    }

    const { pathname } = new URL(request.url);

    // Routes mirror the API's exactly: /articles and /articles/{slug}.
    if (pathname === '/articles' || ARTICLE_SLUG_PATH.test(pathname)) {
      return proxyArticlesRequest(env, pathname);
    }

    return new Response('Not found', { status: 404, headers: CORS_HEADERS });
  },
};
