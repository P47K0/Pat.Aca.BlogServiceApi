import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { TagList } from './TagList';

/** How many of the most-used tags show on the homepage by default. Past
 * this, "Show all" is needed to see the rest. */
const VISIBLE_TAG_COUNT = 20;

/** Homepage tag cloud. Showing every unique tag unconditionally used to
 * push the article list below the fold (118+ tags at last count, many
 * single-use imports) - see backlog. Now shows only the most-used tags by
 * default, with a "Show all" button that reveals the full list in a
 * native <popover> modal (open/close/light-dismiss/Escape all come from
 * the browser, no client-side JS needed - matches this Worker's
 * no-framework-JS approach). */
export const TagCloud: FC<{ articles: Pick<Article, 'tags'>[] }> = ({ articles }) => {
  const counts = new Map<string, number>();
  for (const article of articles) {
    for (const tag of article.tags) {
      counts.set(tag, (counts.get(tag) ?? 0) + 1);
    }
  }
  if (counts.size === 0) return null;

  const allTags = [...counts.keys()].sort();
  const topTags = [...counts.entries()]
    .sort(([tagA, countA], [tagB, countB]) => countB - countA || tagA.localeCompare(tagB))
    .slice(0, VISIBLE_TAG_COUNT)
    .map(([tag]) => tag)
    .sort();
  const hasMore = allTags.length > topTags.length;

  return (
    <div class="mb-8">
      <TagList tags={topTags} />
      {hasMore && (
        <>
          <button
            popovertarget="all-tags-popover"
            class="mt-3 text-xs font-medium text-blue-600 hover:text-blue-700 hover:underline"
          >
            Show all {allTags.length} tags
          </button>
          <div
            id="all-tags-popover"
            popover="auto"
            class="m-auto max-h-[70vh] w-[calc(100%-2rem)] max-w-2xl overflow-y-auto rounded-2xl bg-white p-6 shadow-lg backdrop:bg-black/40"
          >
            <div class="mb-4 flex items-center justify-between">
              <h2 class="text-sm font-semibold text-gray-900">All tags</h2>
              <button
                popovertarget="all-tags-popover"
                popovertargetaction="hide"
                aria-label="Close"
                class="text-lg leading-none text-gray-400 hover:text-gray-600"
              >
                &#x2715;
              </button>
            </div>
            <TagList tags={allTags} />
          </div>
        </>
      )}
    </div>
  );
};
