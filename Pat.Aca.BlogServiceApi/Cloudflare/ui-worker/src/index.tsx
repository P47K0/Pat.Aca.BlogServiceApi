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

app.get('/', async (c) => {
  const articles = await getArticles(c.env);
  return c.html(
    <Layout title="Home">
      <HomePage articles={articles} />
    </Layout>,
  );
});

app.get('/tags/:tag', async (c) => {
  const tag = c.req.param('tag');
  const articles = await getArticles(c.env);
  const filtered = articles.filter((article) => article.tags.includes(tag));
  return c.html(
    <Layout title={`Tagged: ${tag}`}>
      <TagPage tag={tag} articles={filtered} />
    </Layout>,
  );
});

app.get('/articles/:slug', async (c) => {
  const article = await getArticleBySlug(c.env, c.req.param('slug'));
  if (!article) {
    return c.notFound();
  }
  return c.html(
    <Layout title={article.title}>
      <ArticleDetailPage article={article} />
    </Layout>,
  );
});

app.notFound((c) =>
  c.html(
    <Layout title="Not found">
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
    <Layout title="Error">
      <ErrorPage />
    </Layout>,
    502,
  );
});

export default app;
