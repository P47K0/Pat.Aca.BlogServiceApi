// Phase 5: cache invalidation from ACA. Design (backlog item, settled
// 2026-09-12): a single global KV version key bumped by the blog API on
// every Cosmos write that could change KnowledgeBase content, compared
// against each semantic-cache entry's own `cachedAt` timestamp. Coarse
// (whole-cache invalidation) by design, not a per-article reverse index --
// every entry predates any given bump, so one bump makes the entire cache
// stale at once, just expressed as a timestamp comparison instead of an
// outright delete (see semantic-cache.ts's own comment for why that's
// still useful over a hard wipe).
const CONTENT_VERSION_KV_KEY = 'content-updated-at';

/** The content version as of the last bump, or null if it's never been
 * bumped (nothing to compare against yet -- every cache entry is live). */
export async function getContentVersion(kv: KVNamespace): Promise<string | null> {
  return kv.get(CONTENT_VERSION_KV_KEY);
}

/** Bumps the content version to "now". Called from POST
 * /internal/invalidate-cache, itself called by ACA right after a successful
 * article create/update -- see that endpoint's own comment in index.ts for
 * the auth on this. */
export async function bumpContentVersion(kv: KVNamespace): Promise<void> {
  await kv.put(CONTENT_VERSION_KV_KEY, new Date().toISOString());
}
