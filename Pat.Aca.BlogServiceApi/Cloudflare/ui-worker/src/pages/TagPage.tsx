import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';

export const TagPage: FC<{ tag: string; articles: Article[] }> = ({ tag, articles }) => (
  <>
    <h1 class="mb-1 text-2xl font-bold">Tagged “{tag}”</h1>
    <a href="/" class="text-sm text-gray-500 transition hover:text-blue-600">
      ← All articles
    </a>
    <div class="mt-6">
      {articles.length === 0 ? (
        <p class="text-gray-500">No articles with this tag.</p>
      ) : (
        articles.map((article) => <ArticleCard article={article} />)
      )}
    </div>
  </>
);
