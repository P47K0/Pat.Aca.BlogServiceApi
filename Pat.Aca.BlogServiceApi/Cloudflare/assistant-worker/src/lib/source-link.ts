import type { KnowledgeBaseChunk } from './cosmos-client';
import { LANGUAGE_REDIRECT_MESSAGE } from './generation';

/** Minimum cosine similarity the best chunk must clear before an answer gets
 * a "read more" article link (homepage sections are decided by
 * pickSection instead). Deliberately stricter than recent-answers.ts's
 * MIN_GROUNDING_SCORE: a missing link costs nothing, a link under a
 * greeting, refusal or off-topic reply (which still retrieve *something*)
 * looks broken. Starting guess, unvalidated against real traffic -- same
 * caveat as the Worker's other thresholds. */
const MIN_SOURCE_LINK_SCORE = 0.6;

// The Unlisted CV article behind /about.md: embedded so /ask can answer from
// it, but never a page to send visitors to (see article-search.ts's
// EXCLUDED_SLUG for the same exclusion on /search) -- it maps to a homepage
// section instead.
const ABOUT_SLUG = 'about';

/** Section ids on the homepage (www.koorevaar.com), where the widget itself
 * lives. Anything else is dropped rather than passed to the browser. */
const SECTION_ANCHOR_PATTERN = /^[a-z][a-z-]*-section$/;

export type SourceLink =
  | { type: 'article'; slug: string; url: string }
  | { type: 'section'; anchor: string };

/** A homepage section link needs this many `about` chunks under the same
 * section among the retrieved ones, or a single chunk at or above
 * SECTION_SINGLE_CHUNK_SCORE. Real bug found 2026-10-10 replaying retrieval
 * for live questions: the `about` article is chunked per paragraph, so its
 * `### "Ask about Patrick" chat assistant` heading chunk is the top hit for
 * almost any question containing "Patrick" (0.62 for "What certifications
 * does Patrick have?"), which gave a wrong link to the AI section, while the
 * right chunk for "production AKS clusters" scored 0.593 and was dropped by
 * the plain 0.6 floor. Agreement between chunks is the signal that holds up;
 * a lone chunk must clear a higher bar. Both numbers come from a handful of
 * replayed questions, not real traffic -- same caveat as the other
 * thresholds here. */
const SECTION_MIN_AGREEING_CHUNKS = 2;
const SECTION_SINGLE_CHUNK_SCORE = 0.7;

/** The homepage section the retrieved `about` chunks agree on, or null. Sums
 * the scores of the anchored `about` chunks per section and takes the
 * highest total, so a lone attractor chunk loses to two modest ones. */
function pickSection(chunks: KnowledgeBaseChunk[]): string | null {
  const bySection = new Map<string, { total: number; count: number; best: number }>();
  for (const chunk of chunks) {
    const anchor = chunk.sectionAnchor;
    if (chunk.sourceSlug !== ABOUT_SLUG || !anchor || !SECTION_ANCHOR_PATTERN.test(anchor)) {
      continue;
    }
    const entry = bySection.get(anchor) ?? { total: 0, count: 0, best: 0 };
    entry.total += chunk.score;
    entry.count += 1;
    entry.best = Math.max(entry.best, chunk.score);
    bySection.set(anchor, entry);
  }

  let winner: { anchor: string; total: number; count: number; best: number } | null = null;
  for (const [anchor, entry] of bySection) {
    if (!winner || entry.total > winner.total) {
      winner = { anchor, ...entry };
    }
  }
  if (!winner) {
    return null;
  }
  return winner.count >= SECTION_MIN_AGREEING_CHUNKS || winner.best >= SECTION_SINGLE_CHUNK_SCORE
    ? winner.anchor
    : null;
}

/** The one place an answer is grounded in, or null when no link should be
 * shown: an article page, or -- when the top chunk comes from the `about`
 * article (generated from the homepage) -- the homepage section its chunks
 * agree on, as an anchor the widget scrolls to (the visitor is already on
 * that page, so a URL would be useless). Built Worker-side from real
 * `sourceSlug`/`sectionAnchor` values, never written by the model, so it
 * can't be hallucinated or malformed, and it leaves the internal [1]/[2]
 * chunk numbering out of the answer text (see the citation-leak rule in
 * generation.ts's SYSTEM_PROMPT). Only the top chunk decides which kind of
 * source it is: if a profile fact ranks first there is no page to link to,
 * even when an article chunk ranks second. */
export function pickSourceArticle(
  articleBaseUrl: string,
  answer: string,
  chunks: KnowledgeBaseChunk[],
): SourceLink | null {
  if (answer === LANGUAGE_REDIRECT_MESSAGE) {
    return null;
  }
  const top = chunks[0];
  if (!top || top.sourceType !== 'article' || !top.sourceSlug) {
    return null;
  }
  if (top.sourceSlug === ABOUT_SLUG) {
    // Chunks synced before sectionAnchor existed, or under a heading with no
    // section (About, Key facts, Links), simply get no link.
    const anchor = pickSection(chunks);
    return anchor ? { type: 'section', anchor } : null;
  }
  if (top.score < MIN_SOURCE_LINK_SCORE) {
    return null;
  }
  return { type: 'article', slug: top.sourceSlug, url: `${articleBaseUrl}/${encodeURIComponent(top.sourceSlug)}` };
}
