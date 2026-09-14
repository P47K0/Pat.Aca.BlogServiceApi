import type { Env } from './env';
import { embedText } from './lib/embeddings';
import { findCachedMatch, addCacheEntry } from './lib/semantic-cache';
import { retrieveChunks, type KnowledgeBaseChunk } from './lib/cosmos-client';
import { generateAnswer } from './lib/generation';
import { checkRateLimit } from './lib/rate-limit';
import { verifyTurnstile } from './lib/turnstile';
import { detectFlagReason, logConversation } from './lib/conversation-log';
import { withCors, handlePreflight } from './lib/cors';
import { bumpContentVersion } from './lib/cache-invalidation';
import { getEmbeddingsCount } from './lib/embeddings-count';

const CACHE_INVALIDATION_KEY_HEADER = 'X-Cache-Invalidation-Key';
const EMBEDDINGS_COUNT_KEY_HEADER = 'X-Embeddings-Count-Key';

interface AskRequestBody {
  question?: unknown;
  turnstileToken?: unknown;
  allowLogging?: unknown;
}

/** POST /ask — embeds the question, checks the semantic cache, and on a
 * miss queries KnowledgeBase directly (populating the cache for next time),
 * then generates a grounded answer from whichever chunks were found. */
async function handleAsk(request: Request, env: Env, ctx: ExecutionContext, clientIp: string): Promise<Response> {
  // Cheapest possible fail point: rejects an over-quota IP before spending
  // anything on embedding/retrieval/generation, all of which cost real
  // Workers AI neurons or Cosmos RUs.
  const withinLimit = await checkRateLimit(env.ASSISTANT_CACHE, clientIp);
  if (!withinLimit) {
    return Response.json({ error: 'Too many requests, please try again later.' }, { status: 429 });
  }

  const body = (await request.json().catch(() => null)) as AskRequestBody | null;
  const question = body?.question;
  if (typeof question !== 'string' || question.trim() === '') {
    return Response.json({ error: 'question is required' }, { status: 400 });
  }

  // Stubbed in ahead of the chat widget itself (Phase 7, not yet built):
  // nothing can supply a real token until that widget renders the Turnstile
  // challenge client-side and posts its response here, so this rejects
  // every real request until then -- expected and fine, this Worker isn't
  // reachable from a live UI yet either way. Checked after the cheap
  // question-shape validation above but before any embedding/retrieval/
  // generation spend below.
  const turnstileToken = body?.turnstileToken;
  const turnstileOk = await verifyTurnstile(
    env.TURNSTILE_SECRET_KEY,
    typeof turnstileToken === 'string' ? turnstileToken : '',
    clientIp,
  );
  if (!turnstileOk) {
    return Response.json({ error: 'Turnstile verification failed.' }, { status: 403 });
  }

  const questionEmbedding = await embedText(env.AI, question);

  let chunks: KnowledgeBaseChunk[];
  let cache: 'hit' | 'miss';
  const cached = await findCachedMatch(env.ASSISTANT_CACHE, questionEmbedding);
  if (cached) {
    chunks = cached.chunks;
    cache = 'hit';
  } else {
    chunks = await retrieveChunks(env, questionEmbedding);
    await addCacheEntry(env.ASSISTANT_CACHE, { questionEmbedding, chunks, cachedAt: new Date().toISOString() });
    cache = 'miss';
  }

  const answer = await generateAnswer(env.AI, question, chunks);

  // Not stored by default -- allowLogging is the future chat widget's own
  // opt-in toggle (disclosed plainly next to it, Phase 7 concern, not
  // plumbed anywhere yet) -- except a flagged input is logged regardless,
  // so real abuse of this public endpoint isn't invisible just because
  // nobody opted in. ctx.waitUntil so a slow/failed KV write never delays
  // or breaks the answer actually being returned to the visitor.
  const allowLogging = body?.allowLogging === true;
  const flagReason = detectFlagReason(question);
  if (allowLogging || flagReason) {
    ctx.waitUntil(
      logConversation(env.ASSISTANT_CACHE, {
        question,
        answer,
        cache,
        allowLogging,
        flagReason,
        loggedAt: new Date().toISOString(),
      }).catch((error) => console.error('Conversation logging failed:', error)),
    );
  }

  return Response.json({ answer, chunks, cache });
}

/** POST /internal/invalidate-cache — Phase 5. Called by ACA right after a
 * successful article create/update, never by a browser (no CORS/Turnstile/
 * rate-limit on this route: it's not reachable from the chat widget at all,
 * only from server-to-server calls carrying the shared secret below).
 * Bumps the global content-updated-at version key so semantic-cache.ts
 * treats every entry cached before this moment as stale. */
async function handleInvalidateCache(request: Request, env: Env): Promise<Response> {
  const providedKey = request.headers.get(CACHE_INVALIDATION_KEY_HEADER);
  // An unconfigured secret must fail closed, not be read as "auth disabled"
  // -- same convention as the .NET API's own ApiSecurity.RequireApiKey.
  if (!env.CACHE_INVALIDATION_SECRET || providedKey !== env.CACHE_INVALIDATION_SECRET) {
    return new Response('Unauthorized', { status: 401 });
  }

  await bumpContentVersion(env.ASSISTANT_CACHE);
  return new Response('ok');
}

/** GET /internal/embeddings-count — a total KnowledgeBase document count for
 * a homepage counter on the site, e.g. "N facts and articles indexed".
 * Internal-use-only by design: called by a Worker behind the site's
 * homepage, never by a browser directly, so no CORS is set up for this
 * route. Gated by its own shared secret regardless, since a plain count
 * isn't sensitive but "internal only" was the explicit intent, not "public
 * but unadvertised" -- same fail-closed-if-unconfigured convention as
 * /internal/invalidate-cache. */
async function handleEmbeddingsCount(request: Request, env: Env): Promise<Response> {
  const providedKey = request.headers.get(EMBEDDINGS_COUNT_KEY_HEADER);
  if (!env.EMBEDDINGS_COUNT_SECRET || providedKey !== env.EMBEDDINGS_COUNT_SECRET) {
    return new Response('Unauthorized', { status: 401 });
  }

  const count = await getEmbeddingsCount(env);
  return Response.json({ count });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/healthz') {
      return new Response('ok');
    }

    if (url.pathname === '/ask' && request.method === 'OPTIONS') {
      // The browser's own CORS preflight for the real POST below -- see
      // cors.ts's own comment for why this is needed at all.
      return handlePreflight(env.ALLOWED_ORIGIN);
    }

    if (url.pathname === '/ask' && request.method === 'POST') {
      // Real visitor IP — this Worker is called directly from the browser
      // (fetch from the chat widget's client-side JS), not via a
      // Worker-to-Worker chain like ui-worker -> api-proxy, so
      // CF-Connecting-IP here is already the original edge-set value, no
      // X-Real-Client-Ip forwarding needed.
      const clientIp = request.headers.get('CF-Connecting-IP') ?? 'unknown';
      const response = await handleAsk(request, env, ctx, clientIp);
      // Every response needs CORS headers, not just the success path --
      // see cors.ts's own comment for why error responses matter here too.
      return withCors(response, env.ALLOWED_ORIGIN);
    }

    if (url.pathname === '/internal/invalidate-cache' && request.method === 'POST') {
      return handleInvalidateCache(request, env);
    }

    if (url.pathname === '/internal/embeddings-count' && request.method === 'GET') {
      return handleEmbeddingsCount(request, env);
    }

    return new Response('Not found', { status: 404 });
  },
} satisfies ExportedHandler<Env>;
