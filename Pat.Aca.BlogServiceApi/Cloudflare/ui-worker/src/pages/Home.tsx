import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';
import { TagCloud } from '../components/TagCloud';

export const HomePage: FC<{ articles: Article[] }> = ({ articles }) => {
  return (
    <>
      <TagCloud articles={articles} />
      {articles.length === 0 ? (
        <p class="text-gray-500">No articles yet.</p>
      ) : (
        <div>
          {articles.map((article) => (
            <ArticleCard article={article} />
          ))}
        </div>
      )}
    </>
  );
};
