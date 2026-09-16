export interface Env {
  /** Base URL of the api-proxy Worker (blog-service-worker). Plain config
   * (wrangler.toml [vars]), not a secret — see wrangler.toml for why. */
  API_PROXY_BASE_URL: string;
  /** This Worker's own public base URL (its custom domain), used to build
   * absolute canonical/og:url links and the sitemap — can't be derived from
   * the incoming request alone since Cloudflare's edge may see a different
   * Host header than the public-facing domain. */
  SITE_URL: string;
  /** Turnstile widget site key — public by design (embedded in the rendered
   * HTML), not a secret. Plain [vars] entry. */
  TURNSTILE_SITE_KEY: string;
  /** Turnstile secret key, used server-side to verify a submitted token via
   * Cloudflare's siteverify endpoint. Set via `wrangler secret put
   * TURNSTILE_SECRET_KEY` — never checked into wrangler.toml. */
  TURNSTILE_SECRET_KEY: string;
  /** Base URL of the assistant-worker Worker (ai-assistant.koorevaar.com) —
   * reused here for blog search (GET /search), since KnowledgeBase's article
   * embeddings already live there for the chat assistant. Plain `[vars]`
   * entry, not a secret, same idea as API_PROXY_BASE_URL above. */
  ASSISTANT_WORKER_BASE_URL: string;
  /** Shared secret sent as X-Search-Key when calling assistant-worker's
   * GET /search — server-to-server only (this Worker's own SSR route
   * handler calls it, never the visitor's browser directly), matching that
   * Worker's fail-closed-if-unconfigured convention for its other internal
   * routes. Set via `wrangler secret put SEARCH_SECRET`, never committed to
   * wrangler.toml — deliberately the same name/value as assistant-worker's
   * own SEARCH_SECRET, mirroring how ARTICLES_API_KEY is the identical
   * value/name on both api-proxy and blog-service-api. */
  SEARCH_SECRET: string;
}

/** Shape returned by api-proxy's GET /articles/{slug}/comments — mirrors
 * Pat.Aca.BlogServiceApi's PublicComment exactly: no email/status/llmScore
 * (never meant for public consumption), no articleSlug (implied by the
 * page it's rendered on). */
export interface PublicComment {
  id: string;
  authorName: string;
  text: string;
  createdAt: string;
}

/** Shape returned by the api-proxy Worker's GET /articles and
 * GET /articles/{slug} — same as Pat.Aca.BlogServiceApi's Article, except
 * `content` has already been rendered from Markdown to HTML by api-proxy. */
export interface Article {
  id: number;
  slug: string;
  title: string;
  summary: string;
  /** Rendered HTML, not Markdown — safe to inject directly (see
   * ArticleContent.tsx for the trust rationale). */
  content: string;
  publishedAt: string;
  tags: string[];
  /** Incremented server-side by the API on every GET of this article's
   * detail page — see ArticleDetailPage for where it's shown. */
  viewCount: number;
  /** LinkedIn's own "Embed video only" iframe src for this article's demo
   * video, or null/undefined if it has none. A deliberate dependency on the
   * source LinkedIn post staying up/Public — see ArticleDetailPage. */
  linkedinVideoEmbedUrl?: string | null;
  /** Strict numbered series (e.g. "Debugging Skills" 1/2/3) — both null/
   * undefined for an article that isn't part of one. See ArticleDetailPage
   * for the prev/next nav this drives. */
  seriesName?: string | null;
  seriesOrder?: number | null;
  /** Looser, non-ordinal cross-links to other articles by slug — resolved to
   * full Article objects (title/date) by index.tsx before reaching
   * ArticleDetailPage, since this field alone only carries slugs. */
  relatedSlugs?: string[] | null;
}

/** Thrown when api-proxy returns a non-2xx/404 status or the fetch itself
 * fails; caught by index.tsx's onError handler and shown as a generic error
 * page rather than leaking upstream details to the visitor. */
export class UpstreamError extends Error {}
