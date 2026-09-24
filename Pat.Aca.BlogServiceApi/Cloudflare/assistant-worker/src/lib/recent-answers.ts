import type { KnowledgeBaseChunk } from './cosmos-client';
import { LANGUAGE_REDIRECT_MESSAGE } from './generation';

/** Backs the site's AI-assistant fact box: the last few real, grounded
 * answers /ask has actually generated, shown verbatim with no question
 * attached (an answer only ever contains public information about Patrick,
 * never anything about the visitor, so unlike conversation-log.ts this is
 * safe to draw from all real traffic, not just opted-in conversations -- see
 * the backlog item's own history for the full reasoning). Deliberately a
 * separate KV key from both semantic-cache.ts (caches retrieved chunks, not
 * answer text) and conversation-log.ts (opt-in/flagged only, and logs the
 * question too) -- neither is a fit for this. */
const RECENT_ANSWERS_KV_KEY = 'recent-real-answers';

const MAX_RECENT_ANSWERS = 3;

/** Minimum best-chunk cosine similarity (see cosmos-client.ts's own
 * VectorDistance()-based `score`) a question's retrieval must clear before
 * its answer is considered "really" grounded, rather than a greeting/
 * farewell/off-topic/weak-context reply that still retrieved *something*
 * (retrieveChunks has no relevance cutoff of its own -- it always returns
 * topK chunks regardless of how relevant they actually are). Starting guess
 * only, unvalidated against real traffic -- same caveat as this Worker's
 * other untuned thresholds (semantic-cache.ts's SIMILARITY_THRESHOLD,
 * rate-limit.ts's LIMIT_PER_WINDOW). Revisit once there's a real spread of
 * on-topic vs. greeting/off-topic scores to look at. */
const MIN_GROUNDING_SCORE = 0.5;

export interface RecentAnswer {
  answer: string;
  cachedAt: string;
}

/** True only for an answer worth surfacing in the public fact box -- see
 * MIN_GROUNDING_SCORE's own comment for why a relevance floor is needed at
 * all (embedding-based retrieval runs, and always returns chunks, for every
 * question regardless of topic). Two independent checks, not one: the
 * language redirect is an exact, unparaphrased literal (generation.ts), so
 * a free string-equality check catches it with certainty; everything else
 * non-substantive (greeting, farewell, off-topic, "not enough context") has
 * no fixed wording, so the grounding-score floor is the only signal
 * available for those. */
export function shouldStoreAsRecentAnswer(answer: string, chunks: KnowledgeBaseChunk[]): boolean {
  if (answer === LANGUAGE_REDIRECT_MESSAGE) {
    return false;
  }
  const bestScore = chunks[0]?.score ?? -Infinity;
  return bestScore >= MIN_GROUNDING_SCORE;
}

async function readRecentAnswers(kv: KVNamespace): Promise<RecentAnswer[]> {
  const stored = await kv.get(RECENT_ANSWERS_KV_KEY, 'json');
  return (stored as RecentAnswer[] | null) ?? [];
}

/** Fire-and-forget from handleAsk (ctx.waitUntil), right after generateAnswer
 * returns -- never on the response critical path. Push-to-front + truncate
 * to MAX_RECENT_ANSWERS, most-recent-first; read-then-write, not atomic --
 * same accepted trade-off as semantic-cache.ts's own addCacheEntry at this
 * project's traffic scale (an occasionally-dropped entry is a cosmetic miss
 * for a decorative box, not a correctness bug, so no Durable Object is
 * warranted here). No-ops entirely when shouldStoreAsRecentAnswer says no,
 * without ever reading/writing the KV key. */
export async function addRecentAnswerIfReal(
  kv: KVNamespace,
  answer: string,
  chunks: KnowledgeBaseChunk[],
): Promise<void> {
  if (!shouldStoreAsRecentAnswer(answer, chunks)) {
    return;
  }

  const existing = await readRecentAnswers(kv);
  const updated = [{ answer, cachedAt: new Date().toISOString() }, ...existing].slice(0, MAX_RECENT_ANSWERS);
  await kv.put(RECENT_ANSWERS_KV_KEY, JSON.stringify(updated));
}

/** Serves GET /internal/recent-answers -- answers only, most-recent-first,
 * no question/cachedAt exposed (the endpoint's whole point is "no question,
 * ever"; cachedAt is internal bookkeeping, not something the fact box
 * needs). Pure KV read, no generation involved. */
export async function getRecentAnswers(kv: KVNamespace): Promise<string[]> {
  const entries = await readRecentAnswers(kv);
  return entries.map((entry) => entry.answer);
}
