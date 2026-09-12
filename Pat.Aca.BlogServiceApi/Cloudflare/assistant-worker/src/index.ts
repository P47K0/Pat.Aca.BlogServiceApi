import { embedText } from './lib/embeddings';
import { findCachedMatch } from './lib/semantic-cache';

export interface Env {
  /** Workers AI — query embedding (bge-m3, matching the model already used to
   * embed KnowledgeBase content locally via Ollama) and answer generation. */
  AI: Ai;
  /** Semantic cache of {questionEmbedding, chunks, cachedAt} entries,
   * brute-force cosine-compared against an incoming question before falling
   * through to a live Cosmos VectorDistance() query. */
  ASSISTANT_CACHE: KVNamespace;
}

interface AskRequestBody {
  question?: unknown;
}

/** POST /ask — embeds the question and checks the semantic cache. A hit
 * returns the cached chunks straight away; a miss returns 501 for now, since
 * the live Cosmos KnowledgeBase query that actually resolves a miss is the
 * next commit in this Worker's build sequence (see README.md). */
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

  return Response.json(
    { error: 'not_implemented', message: 'KnowledgeBase retrieval is not wired up yet.' },
    { status: 501 },
  );
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
