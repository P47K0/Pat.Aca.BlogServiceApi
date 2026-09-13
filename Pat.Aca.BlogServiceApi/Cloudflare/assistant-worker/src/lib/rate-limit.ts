// Fixed-window per-IP limit on POST /ask -- the already-live endpoint had no
// abuse protection at all before this (Phase 6's own reason for existing:
// don't ship the public chat widget in Phase 7 before the endpoint it calls
// has at least basic protection against bots/abuse racking up real Workers
// AI costs). Reuses the existing ASSISTANT_CACHE KV namespace under its own
// key prefix rather than provisioning a second namespace -- no new manual
// `wrangler kv namespace create` step, and this Worker's total KV footprint
// stays small enough that one namespace serving two purposes is simpler than
// two namespaces to wire up.
const WINDOW_SECONDS = 600; // 10 minutes
// Starting guess only, not yet tuned against real traffic -- same "needs
// real tuning once built" caveat as semantic-cache.ts's SIMILARITY_THRESHOLD.
const LIMIT_PER_WINDOW = 20;

interface RateLimitState {
  count: number;
  windowStartedAt: number; // epoch ms
}

function rateLimitKey(clientIp: string): string {
  return `ratelimit:${clientIp}`;
}

/** Increments clientIp's request count for the current fixed window and
 * reports whether this request is still within LIMIT_PER_WINDOW. Read-then-
 * write against KV, not atomic -- a race between two near-simultaneous
 * requests from the same IP could let one extra slip through, the same
 * accepted trade-off as semantic-cache.ts's addCacheEntry, and fine at this
 * Worker's expected traffic (a personal-site chat widget, not a public API
 * built to withstand a coordinated flood).
 *
 * The window boundary is windowStartedAt, checked here explicitly -- the
 * expirationTtl on the KV write is just a cleanup backstop so an IP that
 * stops sending requests doesn't leave its key behind forever, it isn't what
 * enforces the window itself. */
export async function checkRateLimit(kv: KVNamespace, clientIp: string): Promise<boolean> {
  const key = rateLimitKey(clientIp);
  const now = Date.now();
  const stored = await kv.get<RateLimitState>(key, 'json');

  const withinCurrentWindow = stored !== null && now - stored.windowStartedAt < WINDOW_SECONDS * 1000;
  const state: RateLimitState = withinCurrentWindow
    ? { count: stored!.count + 1, windowStartedAt: stored!.windowStartedAt }
    : { count: 1, windowStartedAt: now };

  await kv.put(key, JSON.stringify(state), { expirationTtl: WINDOW_SECONDS * 2 });

  return state.count <= LIMIT_PER_WINDOW;
}
