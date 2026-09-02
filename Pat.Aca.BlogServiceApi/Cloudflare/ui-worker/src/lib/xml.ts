/** Escapes text for safe placement inside XML element content (RSS titles/
 * descriptions come from hand-authored article text — e.g. "C# & .NET" —
 * which would otherwise produce malformed/unparseable XML). Not needed by
 * sitemap.xml today: its only free-form-adjacent value is the slug, which is
 * already URL-safe. */
export function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}
