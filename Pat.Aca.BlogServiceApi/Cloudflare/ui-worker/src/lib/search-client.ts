import type { Article, Env } from '../types';
import { getArticles } from './blog-client';

/** One result from assistant-worker's GET /search — just a slug + relevance
 * score, no title/summary (KnowledgeBase chunks don't carry one). See
 * resolve-search-results.ts for joining these against the real article
 * list. */
export interface AssistantSearchResult {
  sourceSlug: string;
  score: number;
}

export type SearchOutcome =
  | { ok: true; results: AssistantSearchResult[] }
  | { ok: false; message: string };

/** Calls assistant-worker's GET /search — server-to-server (this Worker's
 * own SSR route handler calls it, never the visitor's browser directly),
 * authenticated via a shared secret (X-Search-Key, same convention as that
 * Worker's other internal routes) with the real visitor IP forwarded via
 * X-Real-Client-Ip so its rate limiter sees the actual reader, not this
 * Worker's own egress — same forwarding pattern postComment already uses
 * calling blog-service-api. A 429/failure is reported back as a friendly
 * message rather than thrown as an UpstreamError: unlike a broken article
 * fetch, a rate-limited or momentarily-unavailable search shouldn't turn the
 * whole page into the generic 502 error page. */
export async function searchAssistant(env: Env, query: string, clientIp: string): Promise<SearchOutcome> {
  const url = new URL('/search', env.ASSISTANT_WORKER_BASE_URL);
  url.searchParams.set('q', query);

  let response: Response;
  try {
    response = await fetch(url.toString(), {
      headers: {
        'X-Search-Key': env.SEARCH_SECRET,
        'X-Real-Client-Ip': clientIp,
      },
    });
  } catch {
    return { ok: false, message: 'Search is temporarily unavailable. Please try again later.' };
  }

  if (response.status === 429) {
    return { ok: false, message: "You're searching a bit too quickly — please wait a moment and try again." };
  }
  if (!response.ok) {
    return { ok: false, message: 'Search is temporarily unavailable. Please try again later.' };
  }

  const body = (await response.json()) as { results: AssistantSearchResult[] };
  return { ok: true, results: body.results };
}

/** Joins assistant-worker's search results (slug + score only — it has no
 * knowledge of article titles/summaries, see KnowledgeBaseChunk's schema)
 * against the full article list this Worker already fetches elsewhere (tag
 * pages, sitemap), to get real title/summary/date for rendering. Preserves
 * assistant-worker's own relevance ordering. A result slug with no matching
 * article — e.g. a stale embedding left over from a since-deleted article,
 * since the KnowledgeBase-Writer identity has no delete permission on that
 * container — is silently dropped rather than rendered as a broken link. */
export async function resolveSearchResults(env: Env, results: AssistantSearchResult[]): Promise<Article[]> {
  const articles = await getArticles(env);
  const bySlug = new Map(articles.map((article) => [article.slug, article]));
  return results
    .map((result) => bySlug.get(result.sourceSlug))
    .filter((article): article is Article => article !== undefined);
}
