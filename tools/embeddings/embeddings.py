"""Shared bge-m3 embedding client for the batch tools (article embeddings, about
sync, indexing): Cloudflare Workers AI first, the ACA bge-m3 server
(P47K0/Pat.Aca.BgeM3Embeddings) as fallback. Python stdlib only.

    import sys; sys.path.insert(0, "tools/embeddings")
    from embeddings import embed
    vectors = embed(["text 1", "text 2"])   # one list of 1024 floats per text

Both back ends run BAAI/bge-m3 with normalized 1024-dim output, so their
vectors are interchangeable in KnowledgeBase and Vectorize.

Routing, per call of embed():
- Workers AI (@cf/baai/bge-m3 over the REST API) unless one of these holds,
  then the ACA:
  - CF_ACCOUNT_ID / CF_WORKERS_AI_TOKEN are not set;
  - the account's neurons used today (UTC), read from the GraphQL Analytics
    API, are at or above NEURON_LIMIT - NEURON_RESERVE. The visitor assistant
    runs on the same account without a fallback, so batch work leaves it
    NEURON_RESERVE neurons. If the counter can't be read, the ACA is used too;
  - an earlier call in this process fell back (sticky, so a batch doesn't
    retry an exhausted Workers AI for every chunk).
- Fallback on Workers AI's daily-allocation error (4006), 429, 5xx, a timeout
  or a connection error. Every fallback prints one line with the reason.
- The ACA scales to zero: before its first use in a process it is warmed with
  GET /health (timeout ACA_WARM_TIMEOUT, a timeout there is retried once).

Environment:
    CF_ACCOUNT_ID, CF_WORKERS_AI_TOKEN   token with "Workers AI: Read" and
                                         "Account Analytics: Read"
    EMBEDDING_FQDN, EMBEDDING_API_KEY    ACA server (claude-tokens.env)
    NEURON_LIMIT (default 10000), NEURON_RESERVE (default 3000)
"""
import datetime
import json
import os
import socket
import urllib.error
import urllib.request

MODEL = "@cf/baai/bge-m3"
DIMS = 1024
BATCH = 32  # ACA (TEI) max client batch; also used for Workers AI
CF_TIMEOUT = 60
ACA_TIMEOUT = 150
ACA_WARM_TIMEOUT = 150

E = os.environ
_state = {"backend": None, "reason": None, "aca_warm": False}


class _Fallback(Exception):
    pass


def _post(url, headers, body, timeout):
    data = json.dumps(body, separators=(",", ":")).encode()
    r = urllib.request.Request(url, data=data, method="POST",
                               headers={"Content-Type": "application/json", **headers})
    with urllib.request.urlopen(r, timeout=timeout) as resp:
        return json.loads(resp.read())


def _neurons_today():
    """Neurons the whole account used since 00:00 UTC, or None if unknown."""
    now = datetime.datetime.now(datetime.timezone.utc)
    start = now.replace(hour=0, minute=0, second=0, microsecond=0)
    query = """query($a: String!, $s: Time!, $e: Time!) { viewer { accounts(filter: {accountTag: $a}) {
      aiInferenceAdaptiveGroups(limit: 1, filter: {datetime_geq: $s, datetime_leq: $e}) {
        sum { totalNeurons } } } } }"""
    try:
        j = _post("https://api.cloudflare.com/client/v4/graphql",
                  {"Authorization": f"Bearer {E['CF_WORKERS_AI_TOKEN']}"},
                  {"query": query, "variables": {"a": E["CF_ACCOUNT_ID"],
                                                 "s": start.isoformat(), "e": now.isoformat()}}, 30)
        if j.get("errors"):
            raise ValueError(j["errors"][0].get("message"))
        groups = j["data"]["viewer"]["accounts"][0]["aiInferenceAdaptiveGroups"]
        return sum(g["sum"]["totalNeurons"] for g in groups)
    except Exception as e:  # noqa: BLE001 - any failure means "unknown"
        print(f"  embeddings: neuron counter unreadable ({str(e)[:120]})")
        return None


def _choose_backend():
    if not (E.get("CF_ACCOUNT_ID") and E.get("CF_WORKERS_AI_TOKEN")):
        return "aca", "CF_ACCOUNT_ID/CF_WORKERS_AI_TOKEN not set"
    limit = int(E.get("NEURON_LIMIT", "10000"))
    reserve = int(E.get("NEURON_RESERVE", "3000"))
    used = _neurons_today()
    if used is None:
        return "aca", "neuron counter unknown, keeping the visitor reserve safe"
    if used >= limit - reserve:
        return "aca", f"{used:.0f} neurons used today, reserve {reserve} of {limit} kept for visitors"
    return "cf", f"{used:.0f} of {limit} neurons used today"


def _cf(texts):
    url = f"https://api.cloudflare.com/client/v4/accounts/{E['CF_ACCOUNT_ID']}/ai/run/{MODEL}"
    try:
        j = _post(url, {"Authorization": f"Bearer {E['CF_WORKERS_AI_TOKEN']}"}, {"text": texts}, CF_TIMEOUT)
    except urllib.error.HTTPError as e:
        raw = e.read().decode(errors="replace")
        if e.code == 429 or e.code >= 500 or "4006" in raw:
            raise _Fallback(f"Workers AI HTTP {e.code}: {raw[:160]}")
        raise RuntimeError(f"Workers AI HTTP {e.code}: {raw[:300]}") from None
    except (TimeoutError, socket.timeout, urllib.error.URLError, ConnectionError) as e:
        raise _Fallback(f"Workers AI {type(e).__name__}: {e}")
    if not j.get("success", True):
        errs = json.dumps(j.get("errors"))
        if "4006" in errs:
            raise _Fallback(f"Workers AI daily allocation: {errs[:160]}")
        raise RuntimeError(f"Workers AI error: {errs[:300]}")
    return j["result"]["data"]


def _aca_base():
    fqdn = E["EMBEDDING_FQDN"].rstrip("/")
    return fqdn if fqdn.startswith("http") else f"https://{fqdn}"


def _aca(texts):
    auth = {"Authorization": f"Bearer {E['EMBEDDING_API_KEY']}"}
    if not _state["aca_warm"]:
        print("  embeddings: warming the ACA (cold start can take ~90 s)")
        for attempt in range(2):
            try:
                r = urllib.request.Request(f"{_aca_base()}/health", headers=auth)
                with urllib.request.urlopen(r, timeout=ACA_WARM_TIMEOUT):
                    break
            except (TimeoutError, socket.timeout, urllib.error.URLError) as e:
                if attempt == 1:
                    raise
                print(f"  embeddings: ACA /health {type(e).__name__}, retrying")
        _state["aca_warm"] = True
    return _post(f"{_aca_base()}/embed", auth, {"inputs": texts, "normalize": True}, ACA_TIMEOUT)


def _check(vecs, n, backend):
    assert len(vecs) == n and all(len(v) == DIMS for v in vecs), f"{backend}: bad embedding shape"
    return vecs


def embed(texts):
    """bge-m3 vectors (1024 floats, normalized) for texts, in order."""
    if _state["backend"] is None:
        _state["backend"], _state["reason"] = _choose_backend()
        print(f"  embeddings: using {'Workers AI' if _state['backend'] == 'cf' else 'ACA'} ({_state['reason']})")
    out = []
    for i in range(0, len(texts), BATCH):
        part = texts[i:i + BATCH]
        if _state["backend"] == "cf":
            try:
                out += _check(_cf(part), len(part), "Workers AI")
                continue
            except _Fallback as e:
                print(f"  embeddings: FALLBACK to ACA: {e}")
                _state["backend"] = "aca"
        out += _check(_aca(part), len(part), "ACA")
    return out


def backend():
    """'cf', 'aca' or None (nothing embedded yet), for reports."""
    return _state["backend"]


if __name__ == "__main__":
    import sys
    v = embed(sys.argv[1:] or ["quanto custa um site?"])
    print(f"{len(v)} vectors x {len(v[0])} dims via {backend()}")
