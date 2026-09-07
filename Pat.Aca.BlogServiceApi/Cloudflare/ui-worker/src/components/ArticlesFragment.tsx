import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from './ArticleCard';

/** HTML fragment returned by GET /partials/articles — infinite scroll's
 * "load more", not a full page (no <Layout> wrapper). Home.tsx's inline
 * script fetches this, moves #article-cards' children into the real list,
 * and reads the new cursor/has-more off #infinite-scroll-sentinel to decide
 * whether to keep going. Two ids, not one combined blob, so the script never
 * has to parse anything beyond querySelector + dataset. */
export const ArticlesFragment: FC<{
  articles: Article[];
  hasMore: boolean;
  nextCursor: string | null;
}> = ({ articles, hasMore, nextCursor }) => (
  <>
    <div id="article-cards">
      {articles.map((article) => (
        <ArticleCard article={article} />
      ))}
    </div>
    <div
      id="infinite-scroll-sentinel"
      data-has-more={hasMore ? 'true' : 'false'}
      data-next-cursor={nextCursor ?? ''}
    ></div>
  </>
);
