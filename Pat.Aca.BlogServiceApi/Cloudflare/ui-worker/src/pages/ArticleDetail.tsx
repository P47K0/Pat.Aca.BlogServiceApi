import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { TagList } from '../components/TagList';
import { formatDate } from '../lib/format-date';

export const ArticleDetailPage: FC<{ article: Article }> = ({ article }) => (
  <>
    <article class="rounded-2xl bg-white p-8 shadow-sm">
      <h1 class="text-3xl font-bold text-gray-900">{article.title}</h1>
      <p class="mt-2 text-sm text-gray-500">{formatDate(article.publishedAt)}</p>
      {article.tags.length > 0 && (
        <div class="mt-3">
          <TagList tags={article.tags} />
        </div>
      )}
      {/* `content` is HTML rendered server-side by api-proxy from Markdown that
          the user hand-authors directly in Cosmos (never user-submitted input)
          — see blog-service-api-project memory's auth-architecture note.
          Trusted content, so injecting it directly is an accepted trade-off,
          not an oversight. */}
      <div
        class="prose prose-neutral mt-8 max-w-none"
        dangerouslySetInnerHTML={{ __html: article.content }}
      />
    </article>
    <a href="/" class="mt-6 block text-sm text-gray-500 transition hover:text-blue-600">
      ← All articles
    </a>
  </>
);
