/** Records one Analytics Engine data point per /feed.xml fetch, so the RSS
 * subscriber count can be estimated later (tools/feed-subscribers). RSS has
 * no sign-up, so the count is derived from who polls the feed:
 *
 * - aggregator: hosted readers (Feedly, Inoreader, NewsBlur, Feedbin, ...)
 *   fetch once for all their users and say how many in the User-Agent,
 *   e.g. "Feedly/1.0 (+http://www.feedly.com/fetcher.html; 12 subscribers)".
 * - reader: anything else polling the feed, roughly one subscriber per
 *   distinct visitor (self-hosted FreshRSS, NetNewsWire, Thunderbird, ...).
 * - crawler: search engines, link previews, uptime checks; not subscribers.
 *
 * Layout: index1 = kind, blob1 = kind, blob2 = reader name, blob3 = visitor
 * hash, double1 = reported subscribers (0 when the User-Agent has none).
 *
 * The visitor hash is SHA-256 over UTC day + IP + User-Agent, cut to 16 hex
 * chars: no raw IP is stored, and the daily rotation means visitors can only
 * be counted per day, not followed across days. */

const SUBSCRIBERS_PATTERN = /(\d+)\s+(?:subscribers?|readers?)\b/i;
const CRAWLER_PATTERN =
  /bot\b|bot\/|crawl|spider|slurp|preview|monitor|uptime|lighthouse|headless|curl\/|wget\/|python-|go-http-client|okhttp/i;

type FeedFetchKind = 'aggregator' | 'reader' | 'crawler';

const BROWSER_TOKENS = new Set(['Mozilla', 'AppleWebKit', 'KHTML', 'Gecko', 'Chrome', 'Safari', 'Version', 'Firefox']);

/** First non-browser product token of a User-Agent: "Feedly/1.0 (...)" ->
 * "Feedly", "Mozilla/5.0 (compatible; Inoreader/1.0; ...)" -> "Inoreader".
 * Falls back to the first word ("NewsBlur Feed Fetcher - ..."). */
function readerName(userAgent: string): string {
  const product = [...userAgent.matchAll(/(?<![\w.\/-])([A-Za-z][\w.-]*)\/\d/g)]
    .map((m) => m[1])
    .find((token) => !BROWSER_TOKENS.has(token));
  const name = product ?? userAgent.trim().split(/[\/\s(;]/)[0];
  return (name || 'unknown').slice(0, 64);
}

async function visitorHash(day: string, ip: string, userAgent: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${day}|${ip}|${userAgent}`));
  return [...new Uint8Array(digest).slice(0, 8)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

export async function recordFeedFetch(dataset: AnalyticsEngineDataset, request: Request): Promise<void> {
  const userAgent = request.headers.get('User-Agent') ?? '';
  const ip = request.headers.get('CF-Connecting-IP') ?? '';
  const subscribers = Number(SUBSCRIBERS_PATTERN.exec(userAgent)?.[1] ?? 0);
  const kind: FeedFetchKind =
    subscribers > 0 ? 'aggregator' : CRAWLER_PATTERN.test(userAgent) || userAgent === '' ? 'crawler' : 'reader';
  const day = new Date().toISOString().slice(0, 10);

  dataset.writeDataPoint({
    indexes: [kind],
    blobs: [kind, readerName(userAgent), await visitorHash(day, ip, userAgent)],
    doubles: [subscribers],
  });
}

interface FeedFetchRow {
  day: string;
  kind: FeedFetchKind;
  reader: string;
  visitor: string;
  subscribers: number | string;
}

/** One UTC day of the estimate: the total, and what each reader added to it
 * (an aggregator's reported count, or a self-polling reader's visitors). */
export interface DailyEstimate {
  day: string;
  total: number;
  readers: Record<string, number>;
}

/** Estimated subscribers per UTC day, oldest first: per aggregator the
 * highest count it reported that day, plus one per distinct non-crawler
 * visitor. Same logic as tools/feed-subscribers/feed_subscribers.py. */
export function dailyEstimates(rows: FeedFetchRow[]): DailyEstimate[] {
  const days = new Map<string, Record<string, number>>();
  for (const row of rows) {
    if (row.kind === 'crawler') continue;
    const day = row.day.slice(0, 10);
    const readers = days.get(day) ?? {};
    readers[row.reader] =
      row.kind === 'aggregator'
        ? Math.max(readers[row.reader] ?? 0, Number(row.subscribers))
        : (readers[row.reader] ?? 0) + 1;
    days.set(day, readers);
  }
  return [...days.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([day, readers]) => ({ day, total: Object.values(readers).reduce((a, b) => a + b, 0), readers }));
}

/** The last 7 days of estimates. The public count is the highest daily
 * total: a single day undercounts readers that poll less than daily, and the
 * current day is still incomplete. Analytics Engine has no Worker-side read
 * binding, so this goes through the SQL API with an "Account Analytics:
 * Read" token. */
export async function estimateSubscribers(accountId: string, apiToken: string): Promise<DailyEstimate[]> {
  const sql = `
    SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day,
           blob1 AS kind, blob2 AS reader, blob3 AS visitor,
           max(double1) AS subscribers
    FROM rss_feed_fetches
    WHERE timestamp > NOW() - INTERVAL '7' DAY AND blob1 != 'crawler'
    GROUP BY day, kind, reader, visitor`;
  const res = await fetch(`https://api.cloudflare.com/client/v4/accounts/${accountId}/analytics_engine/sql`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${apiToken}` },
    body: sql,
  });
  if (!res.ok) throw new Error(`Analytics Engine SQL responded ${res.status}: ${await res.text()}`);
  const { data } = (await res.json()) as { data: FeedFetchRow[] };
  return dailyEstimates(data);
}
