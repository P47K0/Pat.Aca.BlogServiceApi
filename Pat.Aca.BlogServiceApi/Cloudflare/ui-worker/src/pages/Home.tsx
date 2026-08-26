import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';
import { TagList } from '../components/TagList';

export const HomePage: FC<{ articles: Article[] }> = ({ articles }) => {
  const allTags = [...new Set(articles.flatMap((a) => a.tags))].sort();

  return (
    <>
      {allTags.length > 0 && (
        <div class="mb-8">
          <TagList tags={allTags} />
        </div>
      )}
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
