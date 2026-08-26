import type { FC } from 'hono/jsx';

/** Small pill links to /tags/{tag}, reused on article cards and the detail
 * page. `activeTag`, when set, is rendered as plain text instead of a link
 * (already on that tag's page — nothing to navigate to). */
export const TagList: FC<{ tags: string[]; activeTag?: string }> = ({ tags, activeTag }) => (
  <div class="flex flex-wrap gap-2">
    {tags.map((tag) =>
      tag === activeTag ? (
        <span class="rounded-full bg-blue-600 px-3 py-1 text-xs font-medium text-white">
          {tag}
        </span>
      ) : (
        <a
          href={`/tags/${encodeURIComponent(tag)}`}
          class="rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700 hover:bg-blue-200"
        >
          {tag}
        </a>
      ),
    )}
  </div>
);
