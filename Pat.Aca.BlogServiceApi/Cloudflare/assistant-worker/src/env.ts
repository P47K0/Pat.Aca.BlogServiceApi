export interface Env {
  /** Workers AI — query embedding (bge-m3, matching the model already used to
   * embed KnowledgeBase content locally via Ollama) and answer generation. */
  AI: Ai;
  /** Semantic cache of {questionEmbedding, chunks, cachedAt} entries,
   * brute-force cosine-compared against an incoming question before falling
   * through to a live Cosmos VectorDistance() query. */
  ASSISTANT_CACHE: KVNamespace;
  /** Cosmos account name, e.g. "cosmos-koorevaar" — same account the blog
   * API uses, not a secret. Plain `[vars]` entry. */
  COSMOS_ACCOUNT_NAME: string;
  /** The one browser origin allowed to call POST /ask cross-origin, e.g.
   * "https://www.koorevaar.com" (the chat widget's real home — a separate
   * site/project from this repo's own blog.koorevaar.com). Not a secret,
   * a plain `[vars]` entry; CORS is about telling browsers which caller to
   * trust, not about hiding this value. */
  ALLOWED_ORIGIN: string;
  /** Tenant/client ID of the KnowledgeBase-Reader app registration — a
   * dedicated, read+executeQuery-only identity (see infra/cosmos-db.bicep's
   * knowledgeBaseReaderPrincipalId), deliberately narrower than
   * KnowledgeBase-Writer since this Worker is public-facing. Not secrets in
   * principle (same reasoning as the .NET API's AzureAd TenantId/ClientId),
   * but set as dashboard Secrets alongside the client secret below by
   * choice, not committed to wrangler.toml — see that file's own comment. */
  KNOWLEDGEBASE_READER_TENANT_ID: string;
  KNOWLEDGEBASE_READER_CLIENT_ID: string;
  /** The one of the three that's a real secret — set via the dashboard's
   * Variables and Secrets (or `wrangler secret put
   * KNOWLEDGEBASE_READER_CLIENT_SECRET`), never in wrangler.toml. */
  KNOWLEDGEBASE_READER_CLIENT_SECRET: string;
  /** Secret key for the Turnstile widget verified on POST /ask, set via
   * `wrangler secret put TURNSTILE_SECRET_KEY`, never in wrangler.toml.
   * Originally assumed this would reuse ui-worker's blog.koorevaar.com
   * comment-form widget, but the chat widget actually lives on
   * www.koorevaar.com (ALLOWED_ORIGIN above) — a different site than the
   * blog, so that widget's allowed hostnames need www.koorevaar.com added
   * (or a dedicated widget created instead), whichever the Turnstile
   * dashboard config ends up being. Either way this Worker only ever needs
   * the secret key, never the paired site key -- that belongs wherever the
   * widget actually renders, i.e. the www.koorevaar.com project. */
  TURNSTILE_SECRET_KEY: string;
}
