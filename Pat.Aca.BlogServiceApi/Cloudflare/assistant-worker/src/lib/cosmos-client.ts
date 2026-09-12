import type { Env } from '../env';

const DATABASE_NAME = 'AssistantDB';
const CONTAINER_NAME = 'KnowledgeBase';
// Cosmos REST API version supporting the partitioned-query headers used
// below — matches Microsoft's documented examples for this API surface.
const API_VERSION = '2020-07-15';

interface CachedToken {
  token: string;
  expiresAt: number;
}

// Module-level: reused across requests handled by the same warm Worker
// isolate (a real but unguaranteed win, same caveat as any in-memory cache
// in a serverless runtime — a fresh isolate just starts empty). Refreshed a
// minute early so a request never tries to use a token Azure AD is about to
// reject for having just expired.
let cachedToken: CachedToken | null = null;

/** Client-credentials token for KnowledgeBase-Reader, scoped to this Cosmos
 * account. Same OAuth2 flow already proven working end-to-end for
 * KnowledgeBase-Writer during Phase 1's production verification — called via
 * plain fetch() here instead of a test script, since a Worker has no Azure
 * SDK available to do this for us. */
async function getAccessToken(env: Env): Promise<string> {
  if (cachedToken && cachedToken.expiresAt > Date.now()) {
    return cachedToken.token;
  }

  const scope = `https://${env.COSMOS_ACCOUNT_NAME}.documents.azure.com/.default`;
  const response = await fetch(
    `https://login.microsoftonline.com/${env.KNOWLEDGEBASE_READER_TENANT_ID}/oauth2/v2.0/token`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: env.KNOWLEDGEBASE_READER_CLIENT_ID,
        client_secret: env.KNOWLEDGEBASE_READER_CLIENT_SECRET,
        scope,
      }),
    },
  );

  if (!response.ok) {
    throw new Error(`Azure AD token request failed: ${response.status} ${await response.text()}`);
  }

  const body = (await response.json()) as { access_token: string; expires_in: number };
  cachedToken = { token: body.access_token, expiresAt: Date.now() + (body.expires_in - 60) * 1000 };
  return cachedToken.token;
}

const SOURCE_TYPES = ['article', 'profile'] as const;
type SourceType = (typeof SOURCE_TYPES)[number];

/** One row from a KnowledgeBase retrieval query — only the fields the
 * generation prompt actually needs, not the raw Cosmos document (drops
 * `embedding`, which is large and never shown to the model as text). */
export interface KnowledgeBaseChunk {
  id: string;
  sourceType: SourceType;
  sourceSlug?: string;
  text: string;
  score: number;
}

/** Queries one partition (one sourceType value) for its top-k nearest
 * chunks to `embedding`. Scoped to a single partition deliberately, not a
 * cross-partition query: Cosmos's REST API can't serve a cross-partition
 * ORDER BY the way the official SDKs do (needs partition-key-range
 * negotiation the SDKs handle internally) — found the hard way testing
 * KnowledgeBase directly in Phase 2. The caller (retrieveChunks) queries
 * every sourceType value separately and merges/re-sorts client-side instead.
 *
 * `topK` is inlined into the query text rather than passed as a parameter —
 * safe here since it's always this module's own numeric constant, never
 * user input; Cosmos's TOP clause parameter support is inconsistent enough
 * not to rely on across API versions. */
async function queryPartition(
  env: Env,
  sourceType: SourceType,
  embedding: number[],
  topK: number,
): Promise<KnowledgeBaseChunk[]> {
  const token = await getAccessToken(env);
  const url = `https://${env.COSMOS_ACCOUNT_NAME}.documents.azure.com/dbs/${DATABASE_NAME}/colls/${CONTAINER_NAME}/docs`;

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      // Cosmos REST's AAD auth header is the whole "type=aad&ver=1.0&sig="
      // string, URL-encoded as a unit — confirmed against this exact
      // account during Phase 1's production verification testing.
      Authorization: encodeURIComponent(`type=aad&ver=1.0&sig=${token}`),
      'x-ms-date': new Date().toUTCString(),
      'x-ms-version': API_VERSION,
      'x-ms-documentdb-isquery': 'true',
      'x-ms-documentdb-partitionkey': JSON.stringify([sourceType]),
      'Content-Type': 'application/query+json',
      Accept: 'application/json',
    },
    body: JSON.stringify({
      // Ascending VectorDistance = nearest-first for the cosine distance
      // function configured on KnowledgeBase's vector policy (see
      // infra/cosmos-db.bicep) — not yet confirmed against a real query's
      // actual result order (that's Phase 3's retrieval sanity check,
      // still open); flag and fix here if results come back reversed once
      // this runs against real data.
      query: `SELECT TOP ${topK} c.id, c.sourceType, c.sourceSlug, c.text, VectorDistance(c.embedding, @embedding) AS score
              FROM c ORDER BY VectorDistance(c.embedding, @embedding)`,
      parameters: [{ name: '@embedding', value: embedding }],
    }),
  });

  if (!response.ok) {
    throw new Error(
      `Cosmos query failed for sourceType=${sourceType}: ${response.status} ${await response.text()}`,
    );
  }

  const body = (await response.json()) as { Documents: KnowledgeBaseChunk[] };
  return body.Documents;
}

/** Retrieves the top-k chunks across both KnowledgeBase partitions
 * ("article" and "profile"), queried separately then merged and re-sorted
 * by score client-side — see queryPartition's doc comment for why this
 * can't be a single cross-partition query. */
export async function retrieveChunks(
  env: Env,
  embedding: number[],
  topK = 5,
): Promise<KnowledgeBaseChunk[]> {
  const perPartition = await Promise.all(
    SOURCE_TYPES.map((sourceType) => queryPartition(env, sourceType, embedding, topK)),
  );

  return perPartition
    .flat()
    .sort((a, b) => a.score - b.score)
    .slice(0, topK);
}
