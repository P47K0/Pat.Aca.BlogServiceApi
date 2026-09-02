import { Hono } from 'hono';
import type { Env } from './types';
import { UpstreamError } from './types';
import { getArticleBySlug, getArticles } from './lib/blog-client';
import { Layout } from './components/Layout';
import { HomePage } from './pages/Home';
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
      <ArticleDetailPage article={article} />
    </Layout>,
  );
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
