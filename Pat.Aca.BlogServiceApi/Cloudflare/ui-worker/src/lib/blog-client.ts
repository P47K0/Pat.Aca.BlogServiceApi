import type { Article, Env } from '../types';
import { UpstreamError } from '../types';

async function fetchFromProxy(env: Env, path: string): Promise<Response> {
  const url = new URL(path, env.API_PROXY_BASE_URL);
  return fetch(url.toString(), { headers: { Accept: 'application/json' } });
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
