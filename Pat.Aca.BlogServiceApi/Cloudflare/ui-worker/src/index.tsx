import { Hono } from 'hono';
import { getCookie, setCookie, deleteCookie } from 'hono/cookie';
import type { Article, Env } from './types';
import { UpstreamError } from './types';
import { getArticleBySlug, getArticleMarkdown, getArticles, getArticlesPage, getComments, postComment } from './lib/blog-client';
import { resolveSeriesNav, resolveRelatedArticles } from './lib/related-articles';
import { searchAssistant, resolveSearchResults } from './lib/search-client';
import { verifyTurnstile } from './lib/turnstile';
import { escapeXml } from './lib/xml';
import { Layout } from './components/Layout';
import { ArticlesFragment } from './components/ArticlesFragment';
import { HomePage, LOAD_MORE_PAGE_SIZE } from './pages/Home';
import { TagPage } from './pages/TagPage';
import { ArticleDetailPage } from './pages/ArticleDetail';
import { SearchPage } from './pages/SearchPage';
import { NotFoundPage } from './pages/NotFound';
import { ErrorPage } from './pages/ErrorPage';

const app = new Hono<{ Bindings: Env }>();

// A one-time flash message for the comment form's POST-redirect-GET flow
// (see the POST handler below) -- a short-lived cookie, not a query-string
// param like `?comment=success`. A query-string flash has no way to ever
// get removed from the address bar on its own, so refreshing the page kept
// re-showing "Thanks for your comment..." indefinitely (a real bug, caught
// in production). The cookie is set right before redirecting and deleted
// the moment it's read on the next request, so a refresh after that always
// sees a clean state -- and the visible URL never carries the flash at all.
const COMMENT_FLASH_COOKIE = 'comment_flash';

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

// Blog search — reuses the KnowledgeBase embeddings already built for the
// site's AI assistant (assistant-worker), retrieval + ranking only, no LLM
// generation involved. A plain GET so the query survives in the URL (back
// button, bookmarking, sharing a search link) and works with JS disabled,
// matching this site's existing no-client-JS-except-infinite-scroll
// minimalism. `noindex`: a results page for an arbitrary query string isn't
// something search engines should index, same reasoning as the 404/error
// pages.
app.get('/search', async (c) => {
  const query = (c.req.query('q') ?? '').trim();
  const clientIp = c.req.header('CF-Connecting-IP') ?? '';

  let articles: Article[] = [];
  let errorMessage: string | undefined;
  if (query !== '') {
    const outcome = await searchAssistant(c.env, query, clientIp);
    if (outcome.ok) {
      articles = await resolveSearchResults(c.env, outcome.results);
    } else {
      errorMessage = outcome.message;
    }
  }

  return c.html(
    <Layout
      title={query ? `Search: ${query}` : 'Search'}
      description={SITE_DESCRIPTION}
      canonicalUrl={`${c.env.SITE_URL}/search`}
      noindex
    >
      <SearchPage query={query} articles={articles} errorMessage={errorMessage} />
    </Layout>,
  );
});

// Raw Markdown, 1:1 with what's stored in Cosmos (no cleanup) — for AI
// crawlers/agents and the article page's own "View as Markdown" link.
// Registered *before* the generic `/articles/:slug` route below: Hono's
// plain `:slug` param doesn't stop at ".", so it would otherwise swallow the
// ".md" suffix straight into the slug itself and win the match first (found
// via a real local test — the symptom was api-proxy's own .md route being
// hit with a slug of "...actual-slug.md", round-tripping raw Markdown back
// into this route's JSON-expecting getArticleBySlug and throwing a JSON
// parse error). A regex param mixing a constraint with a literal suffix in
// the same segment (`:slug{[^.]+}.md`) turned out not to work either — still
// silently fell through to `/articles/:slug` in the same local test. What
// does work: one regex param spanning the whole segment (`.+\.md`), with the
// ".md" stripped by hand in the handler.
app.get('/articles/:slugWithMd{.+\\.md}', async (c) => {
  const slug = c.req.param('slugWithMd').replace(/\.md$/, '');
  const markdown = await getArticleMarkdown(c.env, slug);
  if (markdown === null) {
    return c.notFound();
  }
  return c.body(markdown, 200, { 'Content-Type': 'text/markdown; charset=utf-8' });
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

  // Read-and-clear: set only right after the POST handler below redirects
  // back here. Deleting it immediately (not just letting maxAge expire)
  // means a refresh a second later — well within the cookie's own
  // maxAge — still correctly shows nothing, since this request already
  // consumed it.
  let commentStatus: 'success' | 'error' | undefined;
  let commentMessage: string | undefined;
  const flashCookie = getCookie(c, COMMENT_FLASH_COOKIE);
  if (flashCookie) {
    try {
      const flash = JSON.parse(flashCookie) as { status?: string; message?: string };
      if (flash.status === 'success' || flash.status === 'error') {
        commentStatus = flash.status;
        commentMessage = flash.message;
      }
    } catch {
      // Malformed/tampered cookie value -- ignore it, just show no banner.
    }
    deleteCookie(c, COMMENT_FLASH_COOKIE, { path: '/' });
  }

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
        commentStatus={commentStatus}
        commentMessage={commentMessage}
      />
    </Layout>,
  );
});

// Same raw-Markdown mechanism as the per-article .md route above, pointed at
// the fixed "about" slug —
// a CV-like document (skills, certs, side projects) stored as one more
// (Unlisted) article, doubling as an llms.txt-style resource for AI
// crawlers/recruiter-assistants.
app.get('/about.md', async (c) => {
  const markdown = await getArticleMarkdown(c.env, 'about');
  if (markdown === null) {
    return c.notFound();
  }
  return c.body(markdown, 200, { 'Content-Type': 'text/markdown; charset=utf-8' });
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
    setCookie(c, COMMENT_FLASH_COOKIE, JSON.stringify({ status, message }), {
      maxAge: 10,
      path: '/',
      httpOnly: true,
      secure: true,
      sameSite: 'Lax',
    });
    return c.redirect(`/articles/${encodeURIComponent(slug)}`, 303);
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
