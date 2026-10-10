import type { KnowledgeBaseChunk } from './cosmos-client';
import { LANGUAGE_REDIRECT_MESSAGE } from './generation';

/** Minimum cosine similarity the best chunk must clear before an answer gets
 * a "read more" link. Deliberately stricter than recent-answers.ts's
 * MIN_GROUNDING_SCORE: a missing link costs nothing, a link under a
 * greeting, refusal or off-topic reply (which still retrieve *something*)
 * looks broken. Starting guess, unvalidated against real traffic -- same
 * caveat as the Worker's other thresholds. */
const MIN_SOURCE_LINK_SCORE = 0.6;

// The Unlisted CV article behind /about.md: embedded so /ask can answer from
// it, but never a page to send visitors to (see article-search.ts's
// EXCLUDED_SLUG for the same exclusion on /search).
const EXCLUDED_SLUG = 'about';

export interface SourceArticle {
  slug: string;
  url: string;
}

/** The one article an answer is grounded in, or null when no link should be
 * shown. Built Worker-side from the top chunk's real `sourceSlug`, never
 * written by the model, so it can't be hallucinated or malformed, and it
 * leaves the internal [1]/[2] chunk numbering out of the answer text (see
 * the citation-leak rule in generation.ts's SYSTEM_PROMPT). Only the top
 * chunk counts: if a profile fact ranks first there is no article page to
 * link to, even when an article chunk ranks second. */
export function pickSourceArticle(
  articleBaseUrl: string,
  answer: string,
  chunks: KnowledgeBaseChunk[],
): SourceArticle | null {
  if (answer === LANGUAGE_REDIRECT_MESSAGE) {
    return null;
  }
  const top = chunks[0];
  if (!top || top.sourceType !== 'article' || !top.sourceSlug || top.sourceSlug === EXCLUDED_SLUG) {
    return null;
  }
  if (top.score < MIN_SOURCE_LINK_SCORE) {
    return null;
  }
  return { slug: top.sourceSlug, url: `${articleBaseUrl}/${encodeURIComponent(top.sourceSlug)}` };
}
