/** One cached question → retrieved-chunks pairing. `chunks` stays untyped
 * here (`unknown`) since the real KnowledgeBase retrieval shape lands in the
 * next commit (the Cosmos cache-miss path) — this module only needs to store
 * and hand back whatever was cached, not interpret it. */
export interface CacheEntry {
  questionEmbedding: number[];
  chunks: unknown;
  cachedAt: string;
}

// Everything lives under one KV key rather than one entry per key: the
// expected scale is dozens to low hundreds of distinct questions ever asked
// (per the backlog item's own estimate), well within a single KV value's
// 25MiB limit, and it lets a single read do the whole brute-force scan
// without a KV list() call per lookup.
const CACHE_KV_KEY = 'semantic-cache-entries';

// Starting guess only, not yet tuned against real traffic — the backlog item
// flags this explicitly as "needs real tuning once built." Revisit once
// there's enough real question volume to measure false-hit/false-miss rates.
const SIMILARITY_THRESHOLD = 0.9;

/** Standard cosine similarity between two equal-length vectors. Assumes both
 * come from the same embedding model (bge-m3, 1024-dim) — callers never mix
 * vectors from different models/dimensionalities. */
function cosineSimilarity(a: number[], b: number[]): number {
  let dot = 0;
  let normA = 0;
  let normB = 0;
  for (let i = 0; i < a.length; i++) {
    dot += a[i] * b[i];
    normA += a[i] * a[i];
    normB += b[i] * b[i];
  }
  return dot / (Math.sqrt(normA) * Math.sqrt(normB));
}

async function readCacheEntries(kv: KVNamespace): Promise<CacheEntry[]> {
  const stored = await kv.get(CACHE_KV_KEY, 'json');
  return (stored as CacheEntry[] | null) ?? [];
}

/** Brute-force scans the cache for the closest entry to `questionEmbedding`,
 * returning it only if it clears SIMILARITY_THRESHOLD — otherwise null, which
 * the caller treats as a cache miss (falls through to a live Cosmos query).
 * Cache staleness (an edited article invalidating a cached answer) isn't
 * handled here yet — that's the global content-updated-at version key from
 * a later phase, not this lookup. */
export async function findCachedMatch(
  kv: KVNamespace,
  questionEmbedding: number[],
): Promise<CacheEntry | null> {
  const entries = await readCacheEntries(kv);

  let best: CacheEntry | null = null;
  let bestScore = -Infinity;
  for (const entry of entries) {
    const score = cosineSimilarity(questionEmbedding, entry.questionEmbedding);
    if (score > bestScore) {
      best = entry;
      bestScore = score;
    }
  }

  return best && bestScore >= SIMILARITY_THRESHOLD ? best : null;
}
