"""Print blog.koorevaar.com article views per referring site, from the
`article_referrers` Analytics Engine dataset the ui-worker writes on every
article page view (see ui-worker/src/lib/referrer-analytics.ts). Python stdlib
only.

    CF_ACCOUNT_ID=... CF_API_TOKEN=... python3 tools/article-referrers/article_referrers.py [days] [slug]

CF_API_TOKEN needs the "Account Analytics: Read" permission. `days` defaults
to 30; Analytics Engine keeps 90 days. `slug` limits the output to one article.

The source is the Referer host without "www.", else "utm:<utm_source>", else
"direct" (typed URL, bookmark, or an app or browser that sends no Referer).
Crawlers and prefetches are not recorded. The same numbers are served as JSON
by https://blog.koorevaar.com/referrers.json?days=N&slug=...
"""
import json
import os
import sys
import urllib.request
from collections import defaultdict

DATASET = "article_referrers"


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
    days = min(90, int(sys.argv[1])) if len(sys.argv) > 1 else 30
    only_slug = sys.argv[2] if len(sys.argv) > 2 else None
    rows = query(f"""
        SELECT blob1 AS slug, blob2 AS source, sum(_sample_interval) AS views
        FROM {DATASET}
        WHERE timestamp > NOW() - INTERVAL '{days}' DAY
        GROUP BY slug, source""")

    articles = defaultdict(dict)
    for row in rows:
        if only_slug and row["slug"] != only_slug:
            continue
        articles[row["slug"]][row["source"]] = int(float(row["views"]))

    if not articles:
        print(f"No article views in the last {days} days.")
        return
    for slug, sources in sorted(articles.items(), key=lambda item: -sum(item[1].values())):
        print(f"{sum(sources.values()):6}  {slug}")
        for source, views in sorted(sources.items(), key=lambda item: (-item[1], item[0])):
            print(f"{views:12}  {source}")


if __name__ == "__main__":
    main()
