import { Hono } from 'hono';
import type { Env } from './types';
import { UpstreamError } from './types';
import { getArticleBySlug, getArticles, getArticlesPage, getComments, postComment } from './lib/blog-client';
import { resolveSeriesNav, resolveRelatedArticles } from './lib/related-articles';
import { verifyTurnstile } from './lib/turnstile';
import { escapeXml } from './lib/xml';
import { Layout } from './components/Layout';
import { ArticlesFragment } from './components/ArticlesFragment';
import { HomePage, LOAD_MORE_PAGE_SIZE } from './pages/Home';
import { TagPage } from './pages/TagPage';
import { ArticleDetailPage } from './pages/ArticleDetail';
import { NotFoundPage } from './pages/NotFound';
import { ErrorPage } from './pages/ErrorPage';

const app = new Hono<{ Bindings: Env }>();

// Fixed site-level description for pages that aren't about one article (home,
// 404, error) — there's no per-site "tagline" field anywhere to derive this
// from, so it's a plain constant. Tune the copy directly here.
const SITE_DESCRIPTION =
  "Patrick Koorevaar's portfolio and demo site — notes and write-ups on software and infrastructure projects.";

app.get('/', async (c) => {
  const articles = await getArticles(c.env);
  return c.html(
    <Layout title="Home" description={SITE_DESCRIPTION} canonicalUrl={`${c.env.SITE_URL}/`}>
      <HomePage articles={articles} />
    </Layout>,
  );
});

// Infinite scroll's "load more" — fetched by Home.tsx's inline client
// script, not linked/navigated to directly. Returns a bare HTML fragment
// (ArticlesFragment), not a full <Layout> page. `after` is required: the
// client always has a real cursor (the last-rendered article's slug) by
// construction, so a missing one means a malformed request, not a normal
// case worth a graceful fallback.
app.get('/partials/articles', async (c) => {
  const after = c.req.query('after');
  if (!after) {
    return c.text('Missing required "after" query param', 400);
  }

  const page = await getArticlesPage(c.env, LOAD_MORE_PAGE_SIZE, after);
  return c.html(<ArticlesFragment articles={page.articles} hasMore={page.hasMore} nextCursor={page.nextCursor} />);
});

app.get('/tags/:tag', async (c) => {
  const tag = c.req.param('tag');
  const articles = await getArticles(c.env);
  const filtered = articles.filter((article) => article.tags.includes(tag));
  return c.html(
    <Layout
      title={`Tagged: ${tag}`}
      description={`Articles tagged "${tag}" on ${SITE_DESCRIPTION}`}
      canonicalUrl={`${c.env.SITE_URL}/tags/${encodeURIComponent(tag)}`}
    >
      <TagPage tag={tag} articles={filtered} />
    </Layout>,
  );
});

app.get('/articles/:slug', async (c) => {
  const article = await getArticleBySlug(c.env, c.req.param('slug'));
  if (!article) {
    return c.notFound();
  }

  // Series nav / related articles need other articles' titles, which the
  // single-article fetch above doesn't carry (relatedSlugs is just slugs).
  // Only fetched when this article actually references either, so the
  // majority of articles (neither field set) still cost one fetch, same as
  // before this feature.
  const needsArticleList = Boolean(article.seriesName) || Boolean(article.relatedSlugs?.length);
  const allArticles = needsArticleList ? await getArticles(c.env) : [];
  const seriesNav = resolveSeriesNav(article, allArticles);
  const relatedArticles = resolveRelatedArticles(article, allArticles);
  const comments = await getComments(c.env, article.slug);

  // Set only right after the POST handler below redirects back here — a
  // one-time flash message via query string, not page state, so a plain
  // refresh doesn't keep re-showing it (unlike re-rendering directly from
  // the POST handler would).
  const commentStatus = c.req.query('comment');
  const commentMessage = c.req.query('message');

  const canonicalUrl = `${c.env.SITE_URL}/articles/${article.slug}`;
  return c.html(
    <Layout
      title={article.title}
      description={article.summary}
      canonicalUrl={canonicalUrl}
      type="article"
      publishedAt={article.publishedAt}
      tags={article.tags}
      jsonLd={{
        '@context': 'https://schema.org',
        '@type': 'BlogPosting',
        headline: article.title,
        description: article.summary,
        datePublished: article.publishedAt,
        url: canonicalUrl,
        keywords: article.tags.join(', '),
        author: { '@type': 'Person', name: 'Patrick Koorevaar' },
      }}
    >
      <ArticleDetailPage
        article={article}
        seriesNav={seriesNav}
        relatedArticles={relatedArticles}
        comments={comments}
        turnstileSiteKey={c.env.TURNSTILE_SITE_KEY}
        commentStatus={commentStatus === 'success' || commentStatus === 'error' ? commentStatus : undefined}
        commentMessage={commentMessage}
      />
    </Layout>,
  );
});

// The comment form (CommentSection.tsx) posts here as a plain HTML form
// submission, not fetch/AJAX — a classic POST-redirect-GET flow (303, not a
// direct re-render) specifically so refreshing the result page never
// resubmits the comment. Turnstile is verified here, server-side, before
// the submission ever reaches api-proxy/blog-service-api — the widget
// itself only proves a browser solved the challenge, this call is what
// actually confirms that with Cloudflare.
app.post('/articles/:slug/comments', async (c) => {
  const slug = c.req.param('slug');
  const formData = await c.req.formData();
  const authorName = String(formData.get('authorName') ?? '').trim();
  const text = String(formData.get('text') ?? '').trim();
  const email = String(formData.get('email') ?? '').trim();
  const turnstileToken = String(formData.get('cf-turnstile-response') ?? '');

  // The real visitor IP — accurate here since this is the original
  // browser-to-edge hop, unlike a later Worker-to-Worker fetch (see
  // api-proxy's REAL_CLIENT_IP_HEADER doc comment for why that distinction
  // matters). Used both for Turnstile's own remoteip cross-check and
  // forwarded via postComment as X-Real-Client-Ip for the API's rate limiter.
  const clientIp = c.req.header('CF-Connecting-IP') ?? '';

  const redirectTo = (status: 'success' | 'error', message?: string) => {
    const params = new URLSearchParams({ comment: status });
    if (message) params.set('message', message);
    return c.redirect(`/articles/${encodeURIComponent(slug)}?${params.toString()}`, 303);
  };

  const turnstileOk = await verifyTurnstile(c.env.TURNSTILE_SECRET_KEY, turnstileToken, clientIp);
  if (!turnstileOk) {
    return redirectTo('error', 'Verification failed — please try again.');
  }

  const result = await postComment(c.env, slug, clientIp, {
    authorName,
    text,
    email: email || undefined,
  });

  return result.ok ? redirectTo('success') : redirectTo('error', result.message);
});

// Not indexed: transient error/not-found responses, never real content.
app.get('/robots.txt', (c) =>
  c.text(`User-agent: *\nAllow: /\nSitemap: ${c.env.SITE_URL}/sitemap.xml\n`),
);

// Lets search engines discover every article without waiting on crawl-only
// link discovery — built straight from the same getArticles() list the home
// page already fetches, no new data needed.
app.get('/sitemap.xml', async (c) => {
  const articles = await getArticles(c.env);
  const urls = [
    { loc: `${c.env.SITE_URL}/` },
    ...articles.map((article) => ({
      loc: `${c.env.SITE_URL}/articles/${article.slug}`,
      lastmod: article.publishedAt,
    })),
  ];
  const body = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ...urls.map(
      (u) =>
        `  <url><loc>${u.loc}</loc>${
          'lastmod' in u && u.lastmod ? `<lastmod>${u.lastmod.slice(0, 10)}</lastmod>` : ''
        }</url>`,
    ),
    '</urlset>',
    '',
  ].join('\n');
  return c.body(body, 200, { 'Content-Type': 'application/xml; charset=UTF-8' });
});

// Plain RSS 2.0 feed, most-recent-first (getArticles() is already sorted that
// way). Uses `summary` per item, not the full rendered `content` — keeps the
// feed small and avoids re-escaping already-rendered HTML inside XML.
app.get('/feed.xml', async (c) => {
  const articles = await getArticles(c.env);
  const siteUrl = c.env.SITE_URL;
  const items = articles
    .map((article) => {
      const articleUrl = `${siteUrl}/articles/${article.slug}`;
      return `  <item>
    <title>${escapeXml(article.title)}</title>
    <link>${articleUrl}</link>
    <guid isPermaLink="true">${articleUrl}</guid>
    <pubDate>${new Date(article.publishedAt).toUTCString()}</pubDate>
    <description>${escapeXml(article.summary)}</description>
  </item>`;
    })
    .join('\n');
  const body = `<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
<channel>
  <title>koorevaar.com Blog</title>
  <link>${siteUrl}/</link>
  <description>${escapeXml(SITE_DESCRIPTION)}</description>
  <language>en</language>
  <lastBuildDate>${new Date().toUTCString()}</lastBuildDate>
${items}
</channel>
</rss>
`;
  return c.body(body, 200, { 'Content-Type': 'application/rss+xml; charset=UTF-8' });
});

app.notFound((c) =>
  c.html(
    <Layout
      title="Not found"
      description={SITE_DESCRIPTION}
      canonicalUrl={`${c.env.SITE_URL}${c.req.path}`}
      noindex
    >
      <NotFoundPage />
    </Layout>,
    404,
  ),
);

// Catches UpstreamError from blog-client (api-proxy unreachable or erroring)
// and any other unexpected failure — shows a generic error page rather than
// leaking upstream details to the visitor.
app.onError((err, c) => {
  console.error(err instanceof UpstreamError ? `Upstream failure: ${err.message}` : err);
  return c.html(
    <Layout
      title="Error"
      description={SITE_DESCRIPTION}
      canonicalUrl={`${c.env.SITE_URL}${c.req.path}`}
      noindex
    >
      <ErrorPage />
    </Layout>,
    502,
  );
});

export default app;
