import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';
import { TagCloud } from '../components/TagCloud';

/** The homepage only ever shows the most recent articles, not the full
 * catalog - see the "Durable list fallback" backlog item: api-proxy's
 * durable fallback (served when ACA is down/slow) only covers this many, so
 * the normal-path display is capped to match rather than silently showing
 * more some of the time and fewer during an outage. Older articles stay
 * reachable via tag pages, the sitemap, and the RSS feed - just not linked
 * from here. */
const HOMEPAGE_ARTICLE_COUNT = 10;

export const HomePage: FC<{ articles: Article[] }> = ({ articles }) => {
  // TagCloud still draws from the full list (not just what's shown below) -
  // it's a site-wide navigation aid, not a summary of this page's content.
  const visible = articles.slice(0, HOMEPAGE_ARTICLE_COUNT);

  return (
    <>
      <TagCloud articles={articles} />
      {visible.length === 0 ? (
        <p class="text-gray-500">No articles yet.</p>
      ) : (
        <div>
          {visible.map((article) => (
            <ArticleCard article={article} />
          ))}
        </div>
      )}
    </>
  );
};
