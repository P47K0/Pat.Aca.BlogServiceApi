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
   * Not ui-worker's blog.koorevaar.com comment-form widget (that was the
   * original guess, wrong) -- the www.koorevaar.com project already had
   * its own separate Turnstile widget, unrelated to the blog, originally
   * set up for a human-visits counter on that site. Reused here since it
   * was already scoped to the right hostname, rather than adding
   * www.koorevaar.com to a blog-scoped widget or creating a third one.
   * This Worker only ever needs that widget's secret key, never its
   * paired site key -- that belongs wherever the widget actually renders,
   * i.e. the www.koorevaar.com project, not here. */
  TURNSTILE_SECRET_KEY: string;
  /** Shared secret ACA presents (as the X-Cache-Invalidation-Key header) when
   * calling POST /internal/invalidate-cache right after a successful article
   * create/update — the trigger point for Phase 5's cache invalidation, since
   * that's the one write path ACA itself knows about (KnowledgeBase chunk/
   * profile-fact writes happen client-side, outside ACA, per this project's
   * embed-at-write-time convention, and aren't covered by this call). Set via
   * `wrangler secret put CACHE_INVALIDATION_SECRET`, never in wrangler.toml.
   * An empty/missing value fails the endpoint closed (401) rather than
   * silently accepting any caller — mirrors ApiSecurity.RequireApiKey's same
   * fail-closed convention on the .NET side, just enforced here instead
   * since this time the .NET API is the caller, not the callee. */
  CACHE_INVALIDATION_SECRET: string;
  /** Shared secret required (as the X-Embeddings-Count-Key header) on GET
   * /internal/embeddings-count. Internal-use-only endpoint: called by a
   * Worker behind the site's homepage to show a "documents indexed" style
   * counter, never by a browser directly. Set via `wrangler secret put
   * EMBEDDINGS_COUNT_SECRET`, never in wrangler.toml. Same fail-closed
   * convention on an empty/missing value as CACHE_INVALIDATION_SECRET above
   * -- the count itself isn't sensitive, but "internal only" was the
   * explicit design intent, not "public but unadvertised". */
  EMBEDDINGS_COUNT_SECRET: string;
  /** Shared secret required (as the X-Search-Key header) on GET /search.
   * Server-to-server only: called by ui-worker's own SSR route handler for
   * the blog search box, never a browser directly -- so no CORS is set up
   * for this route either, same reasoning as /internal/embeddings-count.
   * Set via `wrangler secret put SEARCH_SECRET`, never in wrangler.toml.
   * Same fail-closed-if-unconfigured convention as the other shared secrets
   * above. Deliberately the same name/value on both Workers (ui-worker holds
   * its own SEARCH_SECRET), mirroring how ARTICLES_API_KEY is the identical
   * value/name on both api-proxy and blog-service-api. */
  SEARCH_SECRET: string;
}
