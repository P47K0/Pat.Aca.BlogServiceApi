// bge-m3, matching the model already used to embed KnowledgeBase content
// locally via Ollama (see the Phase 0 cross-runtime compatibility test in the
// backlog item — same model, both runtimes land in the same vector space,
// cosine similarity ~0.9999 on identical input). Only ever used here for
// query-time embedding; content is embedded locally at authoring time.
const EMBEDDING_MODEL = '@cf/baai/bge-m3';

/** Embeds a single piece of text via Workers AI, returning the raw
 * 1024-dim vector. Workers AI's typed response for this model is a union
 * covering its other input shapes (query-against-contexts, batch requests) —
 * the plain `{ text }` input used here always returns the `data` shape, so a
 * missing `data` array can only mean a real API/response problem, not an
 * expected variant, and is treated as an error rather than silently
 * returning a wrong shape to the caller. */
export async function embedText(ai: Ai, text: string): Promise<number[]> {
  const result = await ai.run(EMBEDDING_MODEL, { text: [text] });
  const data = (result as { data?: number[][] }).data;

  if (!data || data.length === 0) {
    throw new Error('Workers AI returned no embedding data for the given text.');
  }

  return data[0];
}
