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
