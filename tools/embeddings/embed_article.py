"""(Re)write the KnowledgeBase embeddings of one blog article, after a POST/PUT
/articles. Embeds with tools/embeddings (Workers AI first, ACA fallback).
Python stdlib only.

    set -a; . .secrets/claude-tokens.env; set +a
    python3 tools/embeddings/embed_article.py <slug> [--dry-run]

Chunks like every article: one chunk per non-empty blank-line block (the
`# Title` line is paragraph-0) plus the "{title}. {summary}" chunk, ids
{slug}-paragraph-{n} / {slug}-summary in partition "article". The writer
identity can't upsert or delete: PUT to replace, POST on 404. Leftover
{slug}-paragraph-{n} ids past the new count are found by reading and printed as
STALE, to be deleted by hand. For the `about` article use
tools/about-sync/sync_about.py instead (it adds sectionAnchor).

Credentials: ARTICLES_API_KEY, KNOWLEDGEBASE_AZURE_*, plus the embedding
variables listed in embeddings.py.
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "about-sync"))
from sync_about import API, COLL, COSMOS, E, chunks, req, token  # noqa: E402
from embeddings import backend, embed  # noqa: E402
import urllib.parse  # noqa: E402


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 1:
        sys.exit(__doc__)
    slug = args[0]
    if slug == "about":
        sys.exit("use tools/about-sync/sync_about.py --embed-only for the about article")
    s, article = req("GET", f"{API}/articles/{slug}", {"X-Api-Key": E["ARTICLES_API_KEY"]})
    assert s == 200, (s, article)
    paragraphs = chunks(article["content"])
    docs = [(f"{slug}-paragraph-{i}", "paragraph", t) for i, t in enumerate(paragraphs)]
    docs.append((f"{slug}-summary", "summary", f"{article['title']}. {article['summary']}"))
    print(f"{slug}: {len(paragraphs)} paragraphs + summary")
    if "--dry-run" in sys.argv:
        print("dry run, stopping before embedding")
        return

    vecs = embed([d[2] for d in docs])
    kb = token("KNOWLEDGEBASE", f"{COSMOS}/.default")
    h = {
        "Authorization": urllib.parse.quote(f"type=aad&ver=1.0&sig={kb}", safe=""),
        "x-ms-version": "2018-12-31",
        "x-ms-documentdb-partitionkey": '["article"]',
        "Content-Type": "application/json",
    }
    counts = {"replaced": 0, "created": 0}
    for (doc_id, ctype, text), vec in zip(docs, vecs):
        doc = {"id": doc_id, "sourceType": "article", "sourceSlug": slug, "chunkType": ctype,
               "text": text, "embedding": vec}
        s, j = req("PUT", f"{COSMOS}{COLL}/docs/{doc_id}", h, doc)
        if s == 404:
            s, j = req("POST", f"{COSMOS}{COLL}/docs", h, doc)
            if s == 409:
                s, j = req("PUT", f"{COSMOS}{COLL}/docs/{doc_id}", h, doc)
                assert s == 200, (doc_id, s, j)
            else:
                assert s == 201, (doc_id, s, j)
            counts["created"] += 1
        else:
            assert s == 200, (doc_id, s, j)
            counts["replaced"] += 1
    print(f"KnowledgeBase: {counts['replaced']} replaced, {counts['created']} created "
          f"({len(docs)} total, via {backend()})")

    stale, n = [], len(paragraphs)
    while True:
        s, _ = req("GET", f"{COSMOS}{COLL}/docs/{slug}-paragraph-{n}", h)
        if s != 200:
            break
        stale.append(f"{slug}-paragraph-{n}")
        n += 1
    if stale:
        print(f"STALE, delete by hand (partition \"article\"): {', '.join(stale)}")


if __name__ == "__main__":
    main()
