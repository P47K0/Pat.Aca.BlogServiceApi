import type { KnowledgeBaseChunk } from './cosmos-client';
import { getContentVersion } from './cache-invalidation';

/** One cached question → retrieved-chunks pairing. */
export interface CacheEntry {
  questionEmbedding: number[];
  chunks: KnowledgeBaseChunk[];
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
 *
 * Phase 5: entries cached before the last content-updated-at bump (see
 * cache-invalidation.ts) are treated as if they don't exist, rather than
 * risking a stale answer for content that's since been edited. Comparing
 * `cachedAt`/`content-updated-at` as plain strings works because both come
 * from the same `new Date().toISOString()` format, which sorts
 * lexicographically the same as chronologically. Stale entries found this
 * way are dropped from storage here too (lazy cleanup on next access) so a
 * bump doesn't leave dead weight in the KV value forever — same
 * read-then-write, non-atomic acceptance as addCacheEntry below. */
export async function findCachedMatch(
  kv: KVNamespace,
  questionEmbedding: number[],
): Promise<CacheEntry | null> {
  const entries = await readCacheEntries(kv);
  const contentVersion = await getContentVersion(kv);
  const live = contentVersion ? entries.filter((entry) => entry.cachedAt >= contentVersion) : entries;

  if (live.length !== entries.length) {
    await kv.put(CACHE_KV_KEY, JSON.stringify(live));
  }

  let best: CacheEntry | null = null;
  let bestScore = -Infinity;
  for (const entry of live) {
    const score = cosineSimilarity(questionEmbedding, entry.questionEmbedding);
    if (score > bestScore) {
      best = entry;
      bestScore = score;
    }
  }

  return best && bestScore >= SIMILARITY_THRESHOLD ? best : null;
}

/** Appends a new entry after a cache miss is resolved from Cosmos. Read-then-
 * write, not atomic — two concurrent misses for near-identical questions
 * could each append their own entry rather than one winning, an accepted
 * simplification at this cache's expected scale (dozens to low hundreds of
 * distinct questions ever asked), same spirit as this project's other
 * accepted non-atomic trade-offs (e.g. api-proxy's per-colo Cache API). No
 * cap on entry count yet either — revisit if real usage ever makes the
 * single KV value's size a concern. */
export async function addCacheEntry(kv: KVNamespace, entry: CacheEntry): Promise<void> {
  const entries = await readCacheEntries(kv);
  entries.push(entry);
  await kv.put(CACHE_KV_KEY, JSON.stringify(entries));
}
