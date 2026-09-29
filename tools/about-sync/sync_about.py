"""Sync the `about` article (served as /about.md) and its KnowledgeBase embeddings
from the homepage export. Run by Claude Code on request, in the container where
local Ollama is reachable. Python stdlib only.

    git clone --depth 1 https://github.com/P47K0/website.git
    python3 website/scripts/export_about_md.py > about.md
    set -a; . .secrets/claude-tokens.env; set +a
    python3 tools/about-sync/sync_about.py about.md [--dry-run] [--embed-only]

Steps: GET the current article; stop if the content is unchanged. Otherwise PUT
the article with only `content` replaced, then embed every blank-line paragraph
plus the "{title}. {summary}" chunk with bge-m3 and write them to KnowledgeBase
as about-paragraph-{n} / about-summary (PUT to replace, POST on 404).

--embed-only re-writes the embeddings for an article that is already updated,
e.g. after a run that failed halfway through the embedding writes.

The KnowledgeBase writer can't delete: if the new export has fewer paragraphs,
the leftover about-paragraph-{n} ids are printed at the end and have to be
deleted by hand (partition "article"), or /ask keeps retrieving the old text.

Credentials come from the environment (ARTICLES_API_KEY, BLOGAPI_AZURE_*,
KNOWLEDGEBASE_AZURE_*); access tokens are kept in memory only.
"""
import http.client
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

API = "https://blog-service-api.whitewater-2e220d9b.westeurope.azurecontainerapps.io"
API_SCOPE = "api://e8ee7301-50bc-47d4-be05-b82df54b498f/.default"
COSMOS = "https://cosmos-koorevaar.documents.azure.com"
COLL = "/dbs/AssistantDB/colls/KnowledgeBase"
OLLAMA = "http://host.docker.internal:11434/api/embed"
SLUG = "about"
E = os.environ


def req(method, url, headers=None, body=None, tries=6):
    """HTTP call with retries: slow or dropped uploads (seen on a 4G hotspot)
    surface as RemoteDisconnected or as Cosmos's 400 MinRequestBodyDataRate."""
    for attempt in range(tries):
        try:
            s, j = _req(method, url, headers, body)
            if s in (408, 429, 503) or (s == 400 and "MinRequestBodyDataRate" in str(j)):
                raise TimeoutError(f"HTTP {s}")
            return s, j
        except (http.client.RemoteDisconnected, ConnectionError, TimeoutError, urllib.error.URLError) as e:
            if attempt == tries - 1:
                raise
            print(f"  retry {method} {url.rsplit('/', 1)[-1]} after {e}")
            time.sleep(2 * (attempt + 1))


def _req(method, url, headers=None, body=None):
    data = None
    if body is not None:
        data = body if isinstance(body, bytes) else json.dumps(body, separators=(",", ":")).encode()
    r = urllib.request.Request(url, data=data, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(r, timeout=120) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw)
        except ValueError:
            return e.code, raw.decode(errors="replace")


def token(prefix, scope):
    body = urllib.parse.urlencode({
        "grant_type": "client_credentials",
        "client_id": E[f"{prefix}_AZURE_CLIENT_ID"],
        "client_secret": E[f"{prefix}_AZURE_CLIENT_SECRET"],
        "scope": scope,
    }).encode()
    s, j = req("POST", f"https://login.microsoftonline.com/{E[prefix + '_AZURE_TENANT_ID']}/oauth2/v2.0/token",
               {"Content-Type": "application/x-www-form-urlencoded"}, body)
    assert s == 200, (s, j.get("error") if isinstance(j, dict) else j)
    return j["access_token"]


def chunks(content):
    """Same chunking as every other article: one chunk per non-empty
    blank-line-separated block, the `# Title` line being paragraph-0."""
    return [b.strip() for b in re.split(r"\n\s*\n", content) if b.strip()]


def embed(texts):
    s, j = req("POST", OLLAMA, {"Content-Type": "application/json"}, {"model": "bge-m3", "input": texts})
    assert s == 200, (s, j)
    vecs = j["embeddings"]
    assert len(vecs) == len(texts) and all(len(v) == 1024 for v in vecs)
    return vecs


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        sys.exit(__doc__)
    new_content = open(args[0], encoding="utf-8").read()
    dry = "--dry-run" in sys.argv
    embed_only = "--embed-only" in sys.argv

    s, article = req("GET", f"{API}/articles/{SLUG}", {"X-Api-Key": E["ARTICLES_API_KEY"]})
    assert s == 200, (s, article)
    old_count = len(chunks(article["content"]))
    new_chunks = chunks(new_content)
    print(f"old paragraphs {old_count}, new paragraphs {len(new_chunks)}")
    if article["content"] == new_content and not embed_only:
        print("unchanged, nothing to do")
        return
    stale = [f"{SLUG}-paragraph-{i}" for i in range(len(new_chunks), old_count)]
    if dry:
        print("dry run, stopping before writes")
        return

    if embed_only:
        assert article["content"] == new_content, "--embed-only needs the article already updated"
    else:
        put_article(article, new_content)
    write_embeddings(article, new_chunks)
    if stale:
        print(f"STALE, delete by hand (partition \"article\"): {', '.join(stale)}")


def put_article(article, new_content):
    """Full-record PUT: every ArticleWriteRequest field round-tripped from the
    GET, only `content` replaced."""
    fields = ["slug", "title", "summary", "publishedAt", "tags", "linkedinVideoEmbedUrl", "seriesName",
              "seriesOrder", "relatedSlugs", "unlisted", "coverImageUrl", "seoDescription", "seoKeywords"]
    body = {k: article[k] for k in fields}
    body["content"] = new_content
    bearer = token("BLOGAPI", API_SCOPE)
    s, j = req("PUT", f"{API}/articles/{SLUG}",
               {"Authorization": f"Bearer {bearer}", "Content-Type": "application/json"}, body)
    assert s == 200, (s, j)
    assert j["content"] == new_content
    print(f"PUT /articles/{SLUG} -> 200")


def write_embeddings(article, new_chunks):
    docs = [(f"{SLUG}-paragraph-{i}", "paragraph", t) for i, t in enumerate(new_chunks)]
    docs.append((f"{SLUG}-summary", "summary", f"{article['title']}. {article['summary']}"))
    vecs = []
    for i in range(0, len(docs), 16):
        vecs += embed([d[2] for d in docs[i:i + 16]])

    kb = token("KNOWLEDGEBASE", f"{COSMOS}/.default")
    h = {
        "Authorization": urllib.parse.quote(f"type=aad&ver=1.0&sig={kb}", safe=""),
        "x-ms-version": "2018-12-31",
        "x-ms-documentdb-partitionkey": '["article"]',
        "Content-Type": "application/json",
    }
    # The writer identity isn't allowed to upsert (403), so PUT to replace, POST on 404.
    counts = {"replaced": 0, "created": 0}
    for (doc_id, ctype, text), vec in zip(docs, vecs):
        doc = {"id": doc_id, "sourceType": "article", "sourceSlug": SLUG, "chunkType": ctype,
               "text": text, "embedding": vec}
        s, j = req("PUT", f"{COSMOS}{COLL}/docs/{doc_id}", h, doc)
        if s == 404:
            s, j = req("POST", f"{COSMOS}{COLL}/docs", h, doc)
            if s == 409:
                # A retried POST whose first attempt did land: replace instead.
                s, j = req("PUT", f"{COSMOS}{COLL}/docs/{doc_id}", h, doc)
                assert s == 200, (doc_id, s, j)
            else:
                assert s == 201, (doc_id, s, j)
            counts["created"] += 1
        else:
            assert s == 200, (doc_id, s, j)
            counts["replaced"] += 1
    print(f"KnowledgeBase: {counts['replaced']} replaced, {counts['created']} created ({len(docs)} total)")

    for doc_id in (f"{SLUG}-paragraph-0", f"{SLUG}-paragraph-{len(new_chunks) - 1}", f"{SLUG}-summary"):
        s, j = req("GET", f"{COSMOS}{COLL}/docs/{doc_id}", h)
        assert s == 200 and len(j["embedding"]) == 1024, (doc_id, s)
    print("read-back OK")


if __name__ == "__main__":
    main()
