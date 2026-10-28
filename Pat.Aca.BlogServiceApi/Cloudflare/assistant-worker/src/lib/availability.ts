/** Kill switch for the assistant and the blog search. While the key below
 * exists in KV, GET /status reports `available: false` (the homepage widget
 * then hides itself), POST /ask answers 503 and GET /search answers 503
 * (ui-worker turns that into its "Search is temporarily unavailable" text).
 *
 * Two ways to set it:
 *   - automatically, when the Cosmos lookup fails (e.g. the Azure credits ran
 *     out and the account is disabled): markUnavailable() switches everything
 *     off for AUTO_DISABLE_SECONDS, after which the next request tries again
 *   - by hand, to hide the assistant for a while:
 *       wrangler kv key put assistant:disabled 1 --binding ASSISTANT_CACHE --ttl 86400
 *     and to bring it back early:
 *       wrangler kv key delete assistant:disabled --binding ASSISTANT_CACHE
 *
 * Same idea as the neuron budget switch in the gated assistant, minus the
 * counter: here the trigger is a failing dependency, not a spent allowance. */
export const DISABLED_KEY = 'assistant:disabled';
export const AUTO_DISABLE_SECONDS = 4 * 60 * 60;

/** Fails open: if KV itself cannot be read, the assistant stays available
 * and the normal error handling takes over. */
export async function isAssistantDisabled(kv: KVNamespace): Promise<boolean> {
  try {
    return (await kv.get(DISABLED_KEY)) !== null;
  } catch (error) {
    console.error('Availability check failed:', error);
    return false;
  }
}

export async function markUnavailable(kv: KVNamespace): Promise<void> {
  try {
    await kv.put(DISABLED_KEY, new Date().toISOString(), { expirationTtl: AUTO_DISABLE_SECONDS });
  } catch (error) {
    console.error('Marking the assistant unavailable failed:', error);
  }
}

export function unavailableResponse(): Response {
  return Response.json(
    { unavailable: true, message: 'The assistant is currently not available.' },
    { status: 503, headers: { 'Retry-After': '3600' } },
  );
}
