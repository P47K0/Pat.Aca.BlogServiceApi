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
// EXCLUDED_SLUG for the same exclusion on /search) -- it maps to a homepage
// section instead.
const ABOUT_SLUG = 'about';

/** Section ids on the homepage (www.koorevaar.com), where the widget itself
 * lives. Anything else is dropped rather than passed to the browser. */
const SECTION_ANCHOR_PATTERN = /^[a-z][a-z-]*-section$/;

export type SourceLink =
  | { type: 'article'; slug: string; url: string }
  | { type: 'section'; anchor: string };

/** The one place an answer is grounded in, or null when no link should be
 * shown: an article page, or -- when the top chunk comes from the `about`
 * article (generated from the homepage) -- the homepage section it was
 * exported from, as an anchor the widget scrolls to (the visitor is already
 * on that page, so a URL would be useless). Built Worker-side from the top chunk's real `sourceSlug`, never
 * written by the model, so it can't be hallucinated or malformed, and it
 * leaves the internal [1]/[2] chunk numbering out of the answer text (see
 * the citation-leak rule in generation.ts's SYSTEM_PROMPT). Only the top
 * chunk counts: if a profile fact ranks first there is no article page to
 * link to, even when an article chunk ranks second. */
export function pickSourceArticle(
  articleBaseUrl: string,
  answer: string,
  chunks: KnowledgeBaseChunk[],
): SourceLink | null {
  if (answer === LANGUAGE_REDIRECT_MESSAGE) {
    return null;
  }
  const top = chunks[0];
  if (!top || top.sourceType !== 'article' || !top.sourceSlug || top.score < MIN_SOURCE_LINK_SCORE) {
    return null;
  }
  if (top.sourceSlug === ABOUT_SLUG) {
    // Chunks synced before sectionAnchor existed, or under a heading with no
    // section (About, Key facts, Links), simply get no link.
    return top.sectionAnchor && SECTION_ANCHOR_PATTERN.test(top.sectionAnchor)
      ? { type: 'section', anchor: top.sectionAnchor }
      : null;
  }
  return { type: 'article', slug: top.sourceSlug, url: `${articleBaseUrl}/${encodeURIComponent(top.sourceSlug)}` };
}
