export interface Env {
  /** Base URL of the api-proxy Worker (blog-service-worker). Plain config
   * (wrangler.toml [vars]), not a secret — see wrangler.toml for why. */
  API_PROXY_BASE_URL: string;
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
}

/** Thrown when api-proxy returns a non-2xx/404 status or the fetch itself
 * fails; caught by index.tsx's onError handler and shown as a generic error
 * page rather than leaking upstream details to the visitor. */
export class UpstreamError extends Error {}
