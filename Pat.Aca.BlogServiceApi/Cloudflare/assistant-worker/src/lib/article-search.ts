import type { Env } from '../env';
import { retrieveArticleChunkCandidates } from './cosmos-client';

// Large enough to see past the top article's own summary+paragraph chunks
// into whichever other articles are also relevant, small enough to stay one
// fast single-partition Cosmos query -- a starting guess, not yet tuned
// against real search traffic (same "needs real tuning once built" caveat as
// semantic-cache.ts's SIMILARITY_THRESHOLD and rate-limit.ts's
// limitPerWindow values).
const CANDIDATE_POOL_SIZE = 40;

// This exact line is appended to the end of every article's Markdown body
// (see the blog-article-coauthor-byline convention) and gets embedded like
// any other paragraph -- see the Phase 2 chunking design's "one embedding
// per blank-line-separated paragraph" rule. Real bug found + confirmed via
// live diagnostic queries 2026-09-16: for any query mentioning "Claude"
// this one chunk is so dominant it filled the ENTIRE top-60
// nearest-candidates pool, one per article, all tied at virtually the same
// score -- crowding out every genuinely relevant chunk, not just competing
// with them. A post-fetch filter isn't enough (there's nothing real left in
// the pool to fall back to) -- excluded directly in the Cosmos query itself
// via retrieveArticleChunkCandidates's excludeText param instead. Not
// cleaned up at the data layer: KnowledgeBase-Writer has no delete
// permission on this container (a known, separate gap), and this line will
// keep getting embedded on every future article regardless of any
// historical cleanup, so this filter needs to stay regardless.
const EXCLUDED_CHUNK_TEXT = '*Co-authored with Claude.*';

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
  const candidates = await retrieveArticleChunkCandidates(env, embedding, CANDIDATE_POOL_SIZE, EXCLUDED_CHUNK_TEXT);

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
