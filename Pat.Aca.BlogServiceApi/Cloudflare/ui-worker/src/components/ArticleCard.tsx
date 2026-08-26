import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { TagList } from './TagList';
import { formatDate } from '../lib/format-date';

/** Summary card used on the home page and tag pages — title, date, summary,
 * and tags. Never renders `content`; that only appears on the detail page. */
export const ArticleCard: FC<{ article: Article }> = ({ article }) => (
  <article class="mb-5 rounded-2xl bg-white p-6 shadow-sm">
    <a
      href={`/articles/${article.slug}`}
      class="text-lg font-semibold text-gray-900 transition hover:text-blue-600"
    >
      {article.title}
    </a>
    <p class="mt-1 text-sm text-gray-500">{formatDate(article.publishedAt)}</p>
    <p class="mt-2 text-gray-700">{article.summary}</p>
    {article.tags.length > 0 && (
      <div class="mt-3">
        <TagList tags={article.tags} />
      </div>
    )}
  </article>
);
