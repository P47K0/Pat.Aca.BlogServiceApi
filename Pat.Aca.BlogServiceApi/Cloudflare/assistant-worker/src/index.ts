import type { Env } from './env';
import { embedText } from './lib/embeddings';
import { findCachedMatch, addCacheEntry } from './lib/semantic-cache';
import { retrieveChunks } from './lib/cosmos-client';

interface AskRequestBody {
  question?: unknown;
}

/** POST /ask — embeds the question, checks the semantic cache, and on a
 * miss queries KnowledgeBase directly (populating the cache for next time).
 * Guardrails/scoping and the actual generation call are later commits —
 * this still just returns the retrieved chunks as JSON, no answer yet. */
async function handleAsk(request: Request, env: Env): Promise<Response> {
  const body = (await request.json().catch(() => null)) as AskRequestBody | null;
  const question = body?.question;
  if (typeof question !== 'string' || question.trim() === '') {
    return Response.json({ error: 'question is required' }, { status: 400 });
  }

  const questionEmbedding = await embedText(env.AI, question);

  const cached = await findCachedMatch(env.ASSISTANT_CACHE, questionEmbedding);
  if (cached) {
    return Response.json({ chunks: cached.chunks, cache: 'hit' });
  }

  const chunks = await retrieveChunks(env, questionEmbedding);
  await addCacheEntry(env.ASSISTANT_CACHE, {
    questionEmbedding,
    chunks,
    cachedAt: new Date().toISOString(),
  });

  return Response.json({ chunks, cache: 'miss' });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/healthz') {
      return new Response('ok');
    }

    if (url.pathname === '/ask' && request.method === 'POST') {
      return handleAsk(request, env);
    }

    return new Response('Not found', { status: 404 });
  },
} satisfies ExportedHandler<Env>;
