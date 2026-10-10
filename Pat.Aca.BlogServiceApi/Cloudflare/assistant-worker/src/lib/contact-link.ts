export type ContactReason = 'contact' | 'cv' | 'unanswered';

export interface ContactLink {
  type: 'contact';
  url: string;
  reason: ContactReason;
}

// Matched on the visitor's question, never on the model's answer, so a
// prompt-injected or rambling reply can't turn it on. Deliberately specific
// phrases, not bare words like "call" or "available" ("call stack",
// "available in Azure"), a missed contact intent just falls back to the
// guardrail text in SYSTEM_PROMPT while a wrong link under an unrelated
// answer looks broken.
const CONTACT_INTENT =
  /\b(contact(?:\s+(?:him|patrick|you))?|e-?mail\w*|get in touch|reach (?:him|out|patrick)|hire|hiring|freelance|vacanc(?:y|ies)|phone|telephone|call him|available for|bellen|bel hem|mailen|bereiken|benaderen|inhuren|aannemen|vacatures?|telefoon|beschikbaar voor)\b/i;

// The CV is a request-by-form on this site, so a question about getting the
// CV goes to the same place.
const CV_INTENT = /\b(cv|resume|r[eé]sum[eé]|curriculum vitae)\b/i;

/** The fixed contact link for questions that want to reach Patrick, ask for
 * his CV, or that the assistant genuinely couldn't answer (the model's own
 * NO_ANSWER marker, see generation.ts). Built from a Worker var, so there is
 * nothing for the model to invent. A contact link replaces any article or
 * section link: under "I don't have enough information" an article link would
 * contradict the answer. */
export function pickContactLink(contactUrl: string, question: string, unanswered: boolean): ContactLink | null {
  let reason: ContactReason | null = null;
  if (CONTACT_INTENT.test(question)) {
    reason = 'contact';
  } else if (CV_INTENT.test(question)) {
    reason = 'cv';
  } else if (unanswered) {
    reason = 'unanswered';
  }
  return reason ? { type: 'contact', url: contactUrl, reason } : null;
}
