import type { Article, Env, PublicComment } from '../types';
import { UpstreamError } from '../types';

async function fetchFromProxy(env: Env, path: string, accept = 'application/json'): Promise<Response> {
  const url = new URL(path, env.API_PROXY_BASE_URL);
  return fetch(url.toString(), { headers: { Accept: accept } });
}

/** Fetches all articles from api-proxy, newest-first (api-proxy/the API
 * already sort and exclude future-dated articles — no re-filtering here). */
export async function getArticles(env: Env): Promise<Article[]> {
  const response = await fetchFromProxy(env, '/articles');
  if (!response.ok) {
    throw new UpstreamError(`GET /articles failed with status ${response.status}`);
  }
  return response.json();
}

export interface ArticlesPage {
  articles: Article[];
  hasMore: boolean;
  /** Slug to pass back as `after` for the next page — null once hasMore is
   * false. */
  nextCursor: string | null;
}

/** Fetches one page of articles for infinite scroll's "load more" — the
 * homepage's own initial 10 come from getArticles() above instead (already
 * needed there for the tag cloud), this is only for article 11 onward. */
export async function getArticlesPage(env: Env, limit: number, after: string | null): Promise<ArticlesPage> {
  const params = new URLSearchParams({ limit: String(limit) });
  if (after) {
    params.set('after', after);
  }

  const response = await fetchFromProxy(env, `/articles?${params.toString()}`);
  if (!response.ok) {
    throw new UpstreamError(`GET /articles?${params.toString()} failed with status ${response.status}`);
  }

  const articles = (await response.json()) as Article[];
  return {
    articles,
    hasMore: response.headers.get('X-Has-More') === 'true',
    nextCursor: response.headers.get('X-Next-Cursor'),
  };
}

/** Fetches one article by slug, or null if api-proxy/the API returned 404
 * (unknown slug or a future-dated article — both 404 there). */
export async function getArticleBySlug(env: Env, slug: string): Promise<Article | null> {
  const response = await fetchFromProxy(env, `/articles/${encodeURIComponent(slug)}`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new UpstreamError(`GET /articles/${slug} failed with status ${response.status}`);
  }
  return response.json();
}

/** Fetches an article's raw Markdown 1:1 (no cleanup) via api-proxy's own
 * .md route -- a distinct fetch from getArticleBySlug above, which returns
 * api-proxy's rendered-HTML JSON shape instead. Backs both the per-article
 * "View as Markdown" link and the /about.md route (same mechanism, just
 * pointed at the fixed "about" slug). Returns null on a 404 (unknown/
 * future-dated slug), mirroring getArticleBySlug. */
export async function getArticleMarkdown(env: Env, slug: string): Promise<string | null> {
  const response = await fetchFromProxy(env, `/articles/${encodeURIComponent(slug)}.md`, 'text/markdown');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new UpstreamError(`GET /articles/${slug}.md failed with status ${response.status}`);
  }
  return response.text();
}

/** Fetches an article's published comments. Unlike every other fetch in
 * this file, a failure here returns an empty list rather than throwing —
 * comments are supplementary to the article itself (same "a side effect
 * that isn't the primary content shouldn't break the page" reasoning as
 * the API's own best-effort view-count increment), so a comments-service
 * hiccup shouldn't turn a successful article load into a 502 error page. */
export async function getComments(env: Env, slug: string): Promise<PublicComment[]> {
  try {
    const response = await fetchFromProxy(env, `/articles/${encodeURIComponent(slug)}/comments`);
    if (!response.ok) {
      return [];
    }
    return await response.json();
  } catch {
    return [];
  }
}

export interface CommentSubmission {
  authorName: string;
  text: string;
  email?: string;
}

export type PostCommentResult =
  | { ok: true }
  | { ok: false; status: number; message: string };

/** Submits a new comment via api-proxy. clientIp is the real visitor IP
 * (already resolved by the caller from the incoming request's
 * CF-Connecting-IP — see index.tsx's POST handler), forwarded as
 * X-Real-Client-Ip so blog-service-api's per-IP+slug rate limiter sees the
 * actual reader, not this Worker's own egress. */
export async function postComment(
  env: Env,
  slug: string,
  clientIp: string,
  submission: CommentSubmission,
): Promise<PostCommentResult> {
  const url = new URL(`/articles/${encodeURIComponent(slug)}/comments`, env.API_PROXY_BASE_URL);
  const response = await fetch(url.toString(), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Real-Client-Ip': clientIp,
    },
    body: JSON.stringify(submission),
  });

  if (response.status === 201) {
    return { ok: true };
  }

  // The API returns RFC 7807 problem+json for every error status here
  // (400 validation, 404 unknown article, 429 rate-limited) — `detail`
  // carries the human-readable reason; fall back to a generic message if
  // the body isn't shaped as expected for any reason.
  const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
  const message =
    response.status === 429
      ? "You're commenting a bit too quickly — please wait a while and try again."
      : problem?.detail || problem?.title || 'Something went wrong submitting your comment. Please try again later.';

  return { ok: false, status: response.status, message };
}
