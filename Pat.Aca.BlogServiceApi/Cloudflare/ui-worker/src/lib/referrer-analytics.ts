/** Records one Analytics Engine data point per article page view, so the
 * views can be counted per article and per referring site (GET
 * /referrers.json, tools/article-referrers).
 *
 * Layout: index1 = slug, blob1 = slug, blob2 = source. The source is the
 * Referer's host without "www." ("google.com", "linkedin.com",
 * "com.linkedin.android" for an android-app:// referrer), else the
 * utm_source query parameter as "utm:<value>", else "direct". Only the host
 * is kept, never the full referring URL or anything about the visitor.
 *
 * Crawlers (same CRAWLER_PATTERN as the feed count), requests without a
 * User-Agent, HEAD requests and browser prefetches are not recorded. */

import { CRAWLER_PATTERN } from './feed-analytics';

const MAX_SOURCE_LENGTH = 96;

export function referrerSource(request: Request): string {
  const referer = request.headers.get('Referer');
  if (referer) {
    try {
      const url = new URL(referer);
      const host = (url.hostname || url.host || url.protocol).replace(/^www\./, '').toLowerCase();
      if (host) return host.slice(0, MAX_SOURCE_LENGTH);
    } catch {
      // Not a URL; fall through to utm_source / direct.
    }
  }
  const utmSource = new URL(request.url).searchParams.get('utm_source')?.trim().toLowerCase();
  return utmSource ? `utm:${utmSource}`.slice(0, MAX_SOURCE_LENGTH) : 'direct';
}

function isCountedView(request: Request): boolean {
  if (request.method !== 'GET') return false;
  const userAgent = request.headers.get('User-Agent') ?? '';
  if (userAgent === '' || CRAWLER_PATTERN.test(userAgent)) return false;
  const purpose = `${request.headers.get('Sec-Purpose') ?? ''} ${request.headers.get('Purpose') ?? ''}`;
  return !/prefetch|prerender/i.test(purpose);
}

export function recordArticleView(dataset: AnalyticsEngineDataset, request: Request, slug: string): void {
  if (!isCountedView(request)) return;
  dataset.writeDataPoint({ indexes: [slug], blobs: [slug, referrerSource(request)] });
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
