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
  /** Pre-fills the header's own search box with the current query — set by
   * the /search route so a visitor lands there without seeing what looks
   * like two separate, empty search inputs (this header box, plus one that
   * used to live on SearchPage.tsx itself, since removed for that reason).
   * Every other page leaves this unset (empty box), which is correct there
   * too — there's no "current query" outside the results page. */
  searchQuery?: string;
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
  searchQuery = '',
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
        {/* Relative href resolves against whatever page it's on — no need to
            thread SITE_URL down here just for feed auto-discovery. */}
        <link
          rel="alternate"
          type="application/rss+xml"
          title="koorevaar.com Blog"
          href="/feed.xml"
        />

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
            // unescaped user input. The `<` escape below is a separate,
            // narrower concern: JSON.stringify doesn't escape it, so a
            // title/summary containing the literal string "</script>" would
            // otherwise close this tag early and let the rest of its value
            // be parsed as HTML. `<` is valid inside a JSON string and
            // still parses back to "<" — this only changes how the *script
            // tag's contents* are delimited, not the JSON-LD data itself.
            dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, '\\u003c') }}
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
          <div class="mx-auto flex max-w-2xl items-center justify-between gap-4">
            {/* All three items below share an explicit h-10, rather than
                relying on items-center to line up their differing intrinsic
                content heights (font size, padding, a native form control's
                own box model) -- that approach visibly failed in production
                (confirmed live, not just a stale-deploy guess): a plain
                <input type="search"> still rendered shorter/higher than
                these siblings even with appearance-none and a flex wrapper
                around it. Forcing every item to the same fixed height
                sidesteps that entirely -- each one centers its own content
                (text or icon+text) within an identical 40px box, so there's
                nothing left to be inconsistent across browsers. */}
            <a
              href="/"
              class="inline-flex h-10 shrink-0 items-center text-xl font-semibold transition hover:text-blue-600"
            >
              Blog
            </a>
            {/* Plain GET form, no client-side JS — submits straight to the
                server-rendered /search results page (see SearchPage.tsx). */}
            <form method="get" action="/search" class="flex h-10 min-w-0 flex-1 items-center">
                server-rendered /search results page (see SearchPage.tsx).
                The form itself is a flex container (not just its input) so
                its box height matches its siblings exactly under the outer
                row's items-center — a plain block-level <form> wrapping a
                native <input type="search"> otherwise renders slightly
                taller in some browsers (native search-input chrome), which
                threw off vertical centering against the "Blog" link and
                Home button on either side. appearance-none on the input
                strips that native styling for the same reason. */}
              <input
                type="search"
                name="q"
                value={searchQuery}
                placeholder="Search…"
                aria-label="Search articles"
                class="h-full w-full appearance-none rounded-xl border border-gray-300 px-3 text-sm text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none"
              />
            </form>
            {/* Same button/copy/icon as the koorevaar.com contact page's Home
                link, back to the main site (not this blog's own "/"). */}
            <a
              href="https://www.koorevaar.com"
              class="flex h-10 shrink-0 items-center gap-2 rounded-2xl border border-gray-300 bg-white px-5 font-medium text-gray-700 shadow-sm transition hover:bg-gray-50 hover:text-blue-600"
            >
              {/*<!--Font Awesome Free v7.3.1 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license/free Copyright 2026 Fonticons, Inc.-->*/}
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><path d="M224 24c0-13.3 10.7-24 24-24 145.8 0 264 118.2 264 264 0 13.3-10.7 24-24 24s-24-10.7-24-24c0-119.3-96.7-216-216-216-13.3 0-24-10.7-24-24zM80 96c26.5 0 48 21.5 48 48l0 224c0 26.5 21.5 48 48 48s48-21.5 48-48-21.5-48-48-48c-8.8 0-16-7.2-16-16l0-64c0-8.8 7.2-16 16-16 79.5 0 144 64.5 144 144S255.5 512 176 512 32 447.5 32 368l0-224c0-26.5 21.5-48 48-48zm168 0c92.8 0 168 75.2 168 168 0 13.3-10.7 24-24 24s-24-10.7-24-24c0-66.3-53.7-120-120-120-13.3 0-24-10.7-24-24s10.7-24 24-24z" /></svg>
              Home
            </a>
          </div>
        </header>
        <main class="flex-1 w-full max-w-2xl mx-auto px-6 py-10">{children}</main>
        <footer class="border-t border-gray-200 px-6 py-4 text-center text-sm text-gray-500">
          <div class="flex items-center justify-center gap-1">
            <span>Served by ui-worker via api-proxy · Pat.Aca.BlogServiceApi ·</span>
            <a
              href="/feed.xml"
              class="inline-flex items-center gap-1 transition hover:text-blue-600"
            >
              {/* Same Font Awesome Free icon set as the header's Home button. */}
              {/*<!--Font Awesome Free 6.7.2 by @fontawesome - https://fontawesome.com License - https://fontawesome.com/license/free (Icons: CC BY 4.0, Fonts: SIL OFL 1.1, Code: MIT License) Copyright 2024 Fonticons, Inc.-->*/}
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 448 512" class="h-3.5 w-3.5" fill="currentColor"><path d="M0 64C0 46.3 14.3 32 32 32c229.8 0 416 186.2 416 416c0 17.7-14.3 32-32 32s-32-14.3-32-32C384 253.6 226.4 96 32 96C14.3 96 0 81.7 0 64zM0 416a64 64 0 1 1 128 0A64 64 0 1 1 0 416zM32 160c159.1 0 288 128.9 288 288c0 17.7-14.3 32-32 32s-32-14.3-32-32c0-123.7-100.3-224-224-224c-17.7 0-32-14.3-32-32s14.3-32 32-32z" /></svg>
              RSS
            </a>
          </div>
        </footer>
      </body>
    </html>
  );
};
