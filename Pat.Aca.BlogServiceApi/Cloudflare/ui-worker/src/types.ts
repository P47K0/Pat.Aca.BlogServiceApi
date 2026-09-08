export interface Env {
  /** Base URL of the api-proxy Worker (blog-service-worker). Plain config
   * (wrangler.toml [vars]), not a secret — see wrangler.toml for why. */
  API_PROXY_BASE_URL: string;
  /** This Worker's own public base URL (its custom domain), used to build
   * absolute canonical/og:url links and the sitemap — can't be derived from
   * the incoming request alone since Cloudflare's edge may see a different
   * Host header than the public-facing domain. */
  SITE_URL: string;
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
}

/** Thrown when api-proxy returns a non-2xx/404 status or the fetch itself
 * fails; caught by index.tsx's onError handler and shown as a generic error
 * page rather than leaking upstream details to the visitor. */
export class UpstreamError extends Error {}
