import type { Env } from '../env';
import { countAllChunks } from './cosmos-client';

interface CachedCount {
  count: number;
  cachedAt: number;
}

const CACHE_KV_KEY = 'embeddings-count-cache';

// The count only changes when an article gets written/edited (Phase 5's own
// content-updated-at trigger) or a profile fact gets hand-authored, both
// rare events -- an hour of staleness on a homepage counter is a non-issue,
// and it keeps a public, high-traffic page from costing a live Cosmos round
// trip on every view. Same cost-conscious instinct as this project's other
// KV caches (semantic-cache.ts, rate-limit.ts).
const CACHE_TTL_MS = 60 * 60 * 1000;

/** Total KnowledgeBase document count, refreshed from Cosmos at most once
 * per CACHE_TTL_MS and served from KV the rest of the time. */
export async function getEmbeddingsCount(env: Env): Promise<number> {
  const cached = (await env.ASSISTANT_CACHE.get(CACHE_KV_KEY, 'json')) as CachedCount | null;
  if (cached && Date.now() - cached.cachedAt < CACHE_TTL_MS) {
    return cached.count;
  }

  const count = await countAllChunks(env);
  await env.ASSISTANT_CACHE.put(CACHE_KV_KEY, JSON.stringify({ count, cachedAt: Date.now() }));
  return count;
}
