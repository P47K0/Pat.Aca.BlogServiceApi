"""Print the estimated RSS subscriber count of blog.koorevaar.com per day, from
the `rss_feed_fetches` Analytics Engine dataset the ui-worker writes on every
/feed.xml fetch (see ui-worker/src/lib/feed-analytics.ts). Python stdlib only.

    CF_ACCOUNT_ID=... CF_API_TOKEN=... python3 tools/feed-subscribers/feed_subscribers.py [days]

CF_API_TOKEN needs the "Account Analytics: Read" permission. `days` defaults to 7.

Per UTC day: hosted aggregators count with the subscriber number from their
User-Agent (highest seen that day per aggregator), every feed reader visitor
counts as one. Plain browsers opening the feed and crawlers do not count. It is an estimate: readers that poll less than once a
day are missed on the days they don't poll, and two people behind one IP with
the same reader app count as one. The homepage tile (ui-worker's
/feed-subscribers.json) shows the highest daily total of the last 7 days.
"""
import json
import os
import sys
import urllib.request
from collections import defaultdict

DATASET = "rss_feed_fetches"

# Same set as BROWSER_TOKENS in feed-analytics.ts. Rows written before the
# browser kind existed are kind "reader" with one of these as reader name.
BROWSER_TOKENS = {
    "Mozilla", "AppleWebKit", "KHTML", "Gecko", "Chrome", "Chromium", "Safari", "Version", "Firefox", "Mobile",
    "Edg", "EdgA", "EdgiOS", "OPR", "SamsungBrowser", "CriOS", "FxiOS", "YaBrowser", "Vivaldi",
}


def query(sql):
    request = urllib.request.Request(
        f"https://api.cloudflare.com/client/v4/accounts/{os.environ['CF_ACCOUNT_ID']}/analytics_engine/sql",
        data=sql.encode(),
        headers={"Authorization": f"Bearer {os.environ['CF_API_TOKEN']}"},
        method="POST",
    )
    with urllib.request.urlopen(request) as response:
        return json.load(response)["data"]


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 else 7
    rows = query(f"""
        SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day,
               blob1 AS kind, blob2 AS reader, blob3 AS visitor,
               max(double1) AS subscribers
        FROM {DATASET}
        WHERE timestamp > NOW() - INTERVAL '{days}' DAY AND blob1 IN ('aggregator', 'reader')
        GROUP BY day, kind, reader, visitor
        ORDER BY day
    """)

    aggregators = defaultdict(dict)  # day -> {reader: subscribers}
    readers = defaultdict(lambda: defaultdict(int))  # day -> {reader: visitors}
    for row in rows:
        day = row["day"][:10]
        if row["kind"] == "aggregator":
            current = aggregators[day].get(row["reader"], 0)
            aggregators[day][row["reader"]] = max(current, int(float(row["subscribers"])))
        elif row["reader"] not in BROWSER_TOKENS:
            readers[day][row["reader"]] += 1

    all_days = sorted(set(aggregators) | set(readers))
    if not all_days:
        print(f"No feed fetches in the last {days} days.")
        return
    best = 0
    for day in all_days:
        total = sum(aggregators[day].values()) + sum(readers[day].values())
        best = max(best, total)
        parts = [f"{name} {n}" for name, n in sorted({**readers[day], **aggregators[day]}.items(), key=lambda kv: -kv[1])]
        print(f"{day}  {total:>4}  ({', '.join(parts)})")
    print(f"Highest day: {best} (with the default 7 days, the count the homepage shows)")


if __name__ == "__main__":
    main()
