// Design decided ahead of the chat widget itself (Phase 7): conversation
// logging defaults to not-stored, with an opt-in toggle the future widget
// will disclose plainly next to (that disclosure UI doesn't exist yet, this
// is just the plumbing it will drive) -- except a guardrail-flagged
// (abusive/injection) input gets logged regardless of the toggle, so real
// abuse of the public endpoint isn't invisible just because nobody opted in.

// Starting list only, not exhaustive -- same "needs real tuning against real
// traffic" caveat as this Worker's other unvalidated thresholds
// (semantic-cache.ts's SIMILARITY_THRESHOLD, rate-limit.ts's
// LIMIT_PER_WINDOW). Deliberately a cheap heuristic, not a second
// content-moderation subsystem duplicating
// Pat.Aca.BlogCommentsModerationFunction's own much heavier LLM-scored
// approach -- that exists for public comments actually published on the
// site; this is just deciding what to keep a private record of.
const INJECTION_PATTERNS: RegExp[] = [
  /ignore\s+(all|any|previous|prior|the\s+above)?\s*instructions/i,
  /disregard\s+(all|any|previous|prior|the\s+above)?\s*(instructions|rules)/i,
  /reveal\s+(your|the)?\s*(system\s+)?prompt/i,
  /system\s+prompt/i,
  /you\s+are\s+now/i,
  /new\s+instructions/i,
  /act\s+as\s+(a|an)\b/i,
  /pretend\s+(you\s+are|to\s+be)/i,
  /jailbreak/i,
];

// Same starting-list caveat as INJECTION_PATTERNS above.
const ABUSE_KEYWORDS = ['fuck', 'shit', 'asshole', 'bitch', 'cunt', 'retard'];

/** Cheap regex/keyword scan for the two guardrail-flag categories this
 * Worker's system prompt (generation.ts) already tries to steer around --
 * catches obvious cases for logging purposes, not a claim of catching every
 * prompt-injection attempt or every way to be abusive. Returns null for an
 * ordinary question, which is the common case. */
export function detectFlagReason(question: string): 'injection' | 'abuse' | null {
  if (INJECTION_PATTERNS.some((pattern) => pattern.test(question))) {
    return 'injection';
  }
  const lower = question.toLowerCase();
  if (ABUSE_KEYWORDS.some((word) => lower.includes(word))) {
    return 'abuse';
  }
  return null;
}

export interface ConversationLogEntry {
  question: string;
  answer: string;
  cache: 'hit' | 'miss';
  allowLogging: boolean;
  flagReason: 'injection' | 'abuse' | null;
  loggedAt: string;
}

// One KV key per entry (unlike semantic-cache.ts's single-key-holds-an-array
// style) -- that style fits the cache's own bounded, low-volume estimate
// (dozens to low hundreds of distinct questions ever asked); a log fed by
// "anyone who opts in, plus anyone flagged" doesn't have the same bound (a
// determined abuser could keep sending flagged questions), so one growing
// JSON array under one key risks eventually hitting KV's 25MiB single-value
// cap. expirationTtl bounds total storage automatically instead of needing a
// manual cleanup job.
const LOG_RETENTION_SECONDS = 60 * 60 * 24 * 90; // 90 days, a starting guess

function logKey(loggedAt: string): string {
  return `conversation-log:${loggedAt}:${crypto.randomUUID()}`;
}

/** Best-effort write -- logging failure must never break the actual answer
 * being returned to the visitor, so callers should let this run without
 * awaiting it into the response critical path failing on its account (see
 * index.ts's own comment at the call site). */
export async function logConversation(kv: KVNamespace, entry: ConversationLogEntry): Promise<void> {
  await kv.put(logKey(entry.loggedAt), JSON.stringify(entry), { expirationTtl: LOG_RETENTION_SECONDS });
}
