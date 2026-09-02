import type { FC, PropsWithChildren } from 'hono/jsx';

export interface SeoProps {
  /** Page-specific title, e.g. an article's title or "Home". Rendered as
   * "{title} · koorevaar.com Blog" in <title>/og:title/twitter:title. */
  title: string;
  /** Plain-text summary for <meta name="description">/og:description —
   * derived from the article's existing `summary` field for article pages,
   * or a fixed site description elsewhere. No new content to author. */
  description: string;
  /** Absolute URL of this page, built from Env.SITE_URL by the caller — used
   * for <link rel="canonical"> and og:url/twitter won't need it. */
  canonicalUrl: string;
  /** 'article' for a single blog post, 'website' (default) for list/tag/error
   * pages — controls og:type and whether article:* tags are emitted. */
  type?: 'website' | 'article';
  /** ISO timestamp, article pages only — emitted as article:published_time
   * and in the JSON-LD block. */
  publishedAt?: string;
  /** Article tags, reused as-is for article:tag + JSON-LD keywords — no
   * separate "SEO keywords" field needed. */
  tags?: string[];
  /** Set on error/not-found pages so search engines don't index them. */
  noindex?: boolean;
  /** Pre-built schema.org object (BlogPosting for articles) serialized as a
   * JSON-LD <script> block. Left to the caller so Layout stays generic. */
  jsonLd?: Record<string, unknown>;
}

export const Layout: FC<PropsWithChildren<SeoProps>> = ({
  title,
  description,
  canonicalUrl,
  type = 'website',
  publishedAt,
  tags,
  noindex,
  jsonLd,
  children,
}) => {
  const fullTitle = `${title} · koorevaar.com Blog`;

  return (
    <html lang="en">
      <head>
        <meta charSet="UTF-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>{fullTitle}</title>
        <meta name="description" content={description} />
        <link rel="canonical" href={canonicalUrl} />
        <meta name="robots" content={noindex ? 'noindex, nofollow' : 'index, follow'} />

        {/* Open Graph — all values come from data the API already returns
            (title/summary/publishedAt/tags), nothing new to author. No
            og:image: no per-article image field exists yet (content images
            are inline in the Markdown body, not a dedicated cover-image
            field) — a real gap for rich social-card previews, deferred. */}
        <meta property="og:type" content={type} />
        <meta property="og:site_name" content="koorevaar.com Blog" />
        <meta property="og:title" content={title} />
        <meta property="og:description" content={description} />
        <meta property="og:url" content={canonicalUrl} />
        {type === 'article' && publishedAt && (
          <meta property="article:published_time" content={publishedAt} />
        )}
        {type === 'article' && tags?.map((tag) => <meta property="article:tag" content={tag} />)}

        <meta name="twitter:card" content="summary" />
        <meta name="twitter:title" content={title} />
        <meta name="twitter:description" content={description} />

        {jsonLd && (
          <script
            type="application/ld+json"
            // Same trusted-content trade-off as ArticleDetailPage's rendered
            // HTML — jsonLd is always built here from API data, never from
            // unescaped user input.
            dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
          />
        )}

        {/* Same favicon as koorevaar.com's other pages (e.g. /contact), for a
            consistent brand identity across the whole domain. */}
        <link
          rel="icon"
          type="image/svg+xml"
          href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'%3E%3Crect width='200' height='200' rx='40' fill='%231e3a8a'/%3E%3Cpath d='M50 70 L100 110 L150 70' stroke='%23dbeafe' stroke-width='18' fill='none' stroke-linecap='round'/%3E%3Crect x='40' y='65' width='120' height='85' rx='15' fill='none' stroke='%23dbeafe' stroke-width='18'/%3E%3C/svg%3E"
        />
        {/* Tailwind Play CDN, per the scaffolding decision — no build step,
            matches api-proxy's no-local-testing minimalism. The `?plugins=typography`
            query loads the prose classes used for rendered article content. */}
        <script src="https://cdn.tailwindcss.com?plugins=typography"></script>
      </head>
      <body class="min-h-screen flex flex-col bg-gray-100 text-gray-900">
        <header class="border-b border-gray-200 px-6 py-4">
          <div class="mx-auto flex max-w-2xl items-center justify-between">
            <a href="/" class="text-xl font-semibold transition hover:text-blue-600">
              Blog
            </a>
            {/* Same button/copy/icon as the koorevaar.com contact page's Home
                link, back to the main site (not this blog's own "/"). */}
            <a
              href="https://www.koorevaar.com"
              class="flex items-center gap-2 rounded-2xl border border-gray-300 bg-white px-5 py-2.5 font-medium text-gray-700 shadow-sm transition hover:bg-gray-50 hover:text-blue-600"
            >
              {/*<!--Font Awesome Free v7.3.1 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license/free Copyright 2026 Fonticons, Inc.-->*/}
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><path d="M224 24c0-13.3 10.7-24 24-24 145.8 0 264 118.2 264 264 0 13.3-10.7 24-24 24s-24-10.7-24-24c0-119.3-96.7-216-216-216-13.3 0-24-10.7-24-24zM80 96c26.5 0 48 21.5 48 48l0 224c0 26.5 21.5 48 48 48s48-21.5 48-48-21.5-48-48-48c-8.8 0-16-7.2-16-16l0-64c0-8.8 7.2-16 16-16 79.5 0 144 64.5 144 144S255.5 512 176 512 32 447.5 32 368l0-224c0-26.5 21.5-48 48-48zm168 0c92.8 0 168 75.2 168 168 0 13.3-10.7 24-24 24s-24-10.7-24-24c0-66.3-53.7-120-120-120-13.3 0-24-10.7-24-24s10.7-24 24-24z" /></svg>
              Home
            </a>
          </div>
        </header>
        <main class="flex-1 w-full max-w-2xl mx-auto px-6 py-10">{children}</main>
        <footer class="border-t border-gray-200 px-6 py-4 text-center text-sm text-gray-500">
          Served by ui-worker via api-proxy · Pat.Aca.BlogServiceApi
        </footer>
      </body>
    </html>
  );
};
