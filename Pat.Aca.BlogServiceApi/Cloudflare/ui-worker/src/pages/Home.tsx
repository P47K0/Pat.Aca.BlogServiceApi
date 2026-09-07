import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';
import { TagCloud } from '../components/TagCloud';
import { INFINITE_SCROLL_CLIENT_SCRIPT } from '../lib/infinite-scroll-client';

/** The homepage's initial render only ever shows this many articles, from
 * the same full-list fetch already needed for the tag cloud below - see the
 * "Durable list fallback" backlog item: api-proxy's durable fallback (served
 * when ACA is down/slow) only covers this many, so the normal-path display
 * is capped to match rather than silently showing more some of the time and
 * fewer during an outage. Beyond this, infinite scroll takes over (see
 * LOAD_MORE_PAGE_SIZE below) - fetched from the API's real cursor
 * pagination, article 11 onward, not sliced from this same list. Older
 * articles also stay reachable via tag pages, the sitemap, and the RSS
 * feed regardless of how far infinite scroll's been used. */
const HOMEPAGE_ARTICLE_COUNT = 10;

/** How many articles GET /partials/articles fetches per "load more" —
 * imported by index.tsx's route handler so the two stay in sync. */
export const LOAD_MORE_PAGE_SIZE = 3;

export const HomePage: FC<{ articles: Article[] }> = ({ articles }) => {
  // TagCloud still draws from the full list (not just what's shown below) -
  // it's a site-wide navigation aid, not a summary of this page's content.
  const visible = articles.slice(0, HOMEPAGE_ARTICLE_COUNT);
  const hasMore = articles.length > HOMEPAGE_ARTICLE_COUNT;

  return (
    <>
      <TagCloud articles={articles} />
      {visible.length === 0 ? (
        <p class="text-gray-500">No articles yet.</p>
      ) : (
        <div id="article-list">
          {visible.map((article) => (
            <ArticleCard article={article} />
          ))}
          {hasMore && (
            <div
              id="infinite-scroll-sentinel"
              class="py-4 text-center text-sm text-gray-500"
              data-has-more="true"
              data-next-cursor={visible[visible.length - 1].slug}
            ></div>
          )}
        </div>
      )}
      {/* Same trusted-content trade-off as Layout's jsonLd block — this is
          always our own fixed script source, never user input. The one
          piece of client-side JS on the site, added specifically for
          infinite scroll; see infinite-scroll-client.ts. */}
      {hasMore && <script dangerouslySetInnerHTML={{ __html: INFINITE_SCROLL_CLIENT_SCRIPT }} />}
    </>
  );
};
