/** Formats an ISO `publishedAt` timestamp for display. UTC explicitly, since
 * this runs on the edge (Worker locale/timezone isn't the visitor's). */
export function formatDate(publishedAt: string): string {
  return new Date(publishedAt).toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}
