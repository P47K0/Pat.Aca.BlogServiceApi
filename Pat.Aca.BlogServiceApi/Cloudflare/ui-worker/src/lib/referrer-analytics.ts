/** Records one Analytics Engine data point per article view, so the views
 * can be counted per article and per referring site (GET /referrers.json,
 * tools/article-referrers).
 *
 * A view is counted from the page's own script (VIEW_BEACON_CLIENT_SCRIPT,
 * POST /api/view) once the page is shown, not when this Worker serves the
 * HTML: anything that downloads the page with a browser-like User-Agent used
 * to count, and its Referer header became a "referring site". Visitors with
 * JavaScript off are no longer counted; there are too few to matter.
 *
 * Layout: index1 = slug, blob1 = slug, blob2 = source. The source is the
 * referring page's host without "www." ("google.com", "linkedin.com",
 * "com.linkedin.android" for an android-app:// referrer), else the
 * utm_source query parameter as "utm:<value>", else "direct". Only the host
 * is sent and kept, never the full referring URL or anything about the
 * visitor. */

import { CRAWLER_PATTERN } from './feed-analytics';

const SLUG_PATTERN = /^[a-z0-9][a-z0-9-]{0,159}$/;
const HOST_PATTERN = /^[a-z0-9][a-z0-9.-]{0,95}$/;
const UTM_PATTERN = /^[a-z0-9._-]{1,64}$/;

export interface ArticleView {
  slug: string;
  source: string;
}

/** The referring site as stored in blob2, from the beacon's `ref` (a host)
 * and `utm` (utm_source) values, both already validated. */
export function sourceOf(referrerHost: string, utmSource: string): string {
  if (referrerHost) return referrerHost.replace(/^www\./, '');
  return utmSource ? `utm:${utmSource}` : 'direct';
}

/** The view a beacon reports, or null when it doesn't come from a page of
 * this site (Origin, else Sec-Fetch-Site), comes from a crawler or a client
 * without a User-Agent, or carries values that aren't plain. */
export function parseViewBeacon(request: Request, form: FormData): ArticleView | null {
  const userAgent = request.headers.get('User-Agent') ?? '';
  if (userAgent === '' || CRAWLER_PATTERN.test(userAgent)) return null;

  const origin = request.headers.get('Origin');
  const sameOrigin = origin
    ? origin === new URL(request.url).origin
    : request.headers.get('Sec-Fetch-Site') === 'same-origin';
  if (!sameOrigin) return null;

  const value = (name: string) => {
    const field = form.get(name);
    return typeof field === 'string' ? field.trim().toLowerCase() : '';
  };
  const slug = value('slug');
  const ref = value('ref');
  const utm = value('utm');
  if (!SLUG_PATTERN.test(slug)) return null;
  if (ref && !HOST_PATTERN.test(ref)) return null;
  if (utm && !UTM_PATTERN.test(utm)) return null;
  return { slug, source: sourceOf(ref, utm) };
}

export function recordArticleView(dataset: AnalyticsEngineDataset, view: ArticleView): void {
  dataset.writeDataPoint({ indexes: [view.slug], blobs: [view.slug, view.source] });
}

export interface ArticleReferrers {
  slug: string;
  views: number;
  referrers: { source: string; views: number }[];
}

/** Analytics Engine keeps data for 3 months, so that is the longest window. */
export const MAX_REFERRER_DAYS = 90;

/** Views per article and source over the last `days` days, articles with the
 * most views first, each article's sources likewise. Counts are
 * sum(_sample_interval), so they stay right if Analytics Engine samples. */
export async function articleReferrers(accountId: string, apiToken: string, days: number): Promise<ArticleReferrers[]> {
  const sql = `
    SELECT blob1 AS slug, blob2 AS source, sum(_sample_interval) AS views
    FROM article_referrers
    WHERE timestamp > NOW() - INTERVAL '${days}' DAY
    GROUP BY slug, source`;
  const res = await fetch(`https://api.cloudflare.com/client/v4/accounts/${accountId}/analytics_engine/sql`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${apiToken}` },
    body: sql,
  });
  if (!res.ok) throw new Error(`Analytics Engine SQL responded ${res.status}: ${await res.text()}`);
  const { data } = (await res.json()) as { data: { slug: string; source: string; views: number | string }[] };

  const articles = new Map<string, ArticleReferrers>();
  for (const row of data) {
    const views = Number(row.views);
    const article = articles.get(row.slug) ?? { slug: row.slug, views: 0, referrers: [] };
    article.views += views;
    article.referrers.push({ source: row.source, views });
    articles.set(row.slug, article);
  }
  const result = [...articles.values()].sort((a, b) => b.views - a.views || a.slug.localeCompare(b.slug));
  for (const article of result) article.referrers.sort((a, b) => b.views - a.views || a.source.localeCompare(b.source));
  return result;
}
