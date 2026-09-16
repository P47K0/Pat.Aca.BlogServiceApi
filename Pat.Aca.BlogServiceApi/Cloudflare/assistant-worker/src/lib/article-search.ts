import type { Env } from '../env';
import { retrieveArticleChunkCandidates } from './cosmos-client';

// Large enough to see past the top article's own summary+paragraph chunks
// into whichever other articles are also relevant, small enough to stay one
// fast single-partition Cosmos query -- a starting guess, not yet tuned
// against real search traffic (same "needs real tuning once built" caveat as
// semantic-cache.ts's SIMILARITY_THRESHOLD and rate-limit.ts's
// limitPerWindow values).
const CANDIDATE_POOL_SIZE = 40;

export interface ArticleSearchResult {
  sourceSlug: string;
  score: number;
}

/** Blog search: embeds the visitor's query the same way /ask embeds a
 * question (works equally for a few keywords or a full question -- it's
 * semantic similarity, not literal keyword matching), then retrieves the
 * nearest `sourceType: "article"` chunks. Since an article is chunked into
 * one summary + one-per-paragraph embedding, a single query commonly
 * surfaces several chunks from the same article -- this collapses those down
 * to one result per sourceSlug (keeping only its best-scoring chunk), so a
 * highly-relevant article doesn't crowd the results list by occupying
 * multiple slots with its own chunks. Caller (index.ts's handleSearch) is
 * responsible for resolving sourceSlug to an actual article for display --
 * KnowledgeBase chunks don't carry a title, only sourceSlug. */
export async function searchArticles(env: Env, embedding: number[], limit: number): Promise<ArticleSearchResult[]> {
  const candidates = await retrieveArticleChunkCandidates(env, embedding, CANDIDATE_POOL_SIZE);

  const bestScoreBySlug = new Map<string, number>();
  for (const chunk of candidates) {
    if (!chunk.sourceSlug) {
      continue;
    }
    const best = bestScoreBySlug.get(chunk.sourceSlug);
    if (best === undefined || chunk.score > best) {
      bestScoreBySlug.set(chunk.sourceSlug, chunk.score);
    }
  }

  return [...bestScoreBySlug.entries()]
    .map(([sourceSlug, score]) => ({ sourceSlug, score }))
    .sort((a, b) => b.score - a.score)
    .slice(0, limit);
}
