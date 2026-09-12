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
  /** Tenant/client ID of the KnowledgeBase-Reader app registration — a
   * dedicated, read+executeQuery-only identity (see infra/cosmos-db.bicep's
   * knowledgeBaseReaderPrincipalId), deliberately narrower than
   * KnowledgeBase-Writer since this Worker is public-facing. Not secrets
   * (same reasoning as the .NET API's AzureAd TenantId/ClientId), plain
   * `[vars]` entries. */
  KNOWLEDGEBASE_READER_TENANT_ID: string;
  KNOWLEDGEBASE_READER_CLIENT_ID: string;
  /** The one real secret of the three — set via
   * `wrangler secret put KNOWLEDGEBASE_READER_CLIENT_SECRET`, never in
   * wrangler.toml. */
  KNOWLEDGEBASE_READER_CLIENT_SECRET: string;
}
