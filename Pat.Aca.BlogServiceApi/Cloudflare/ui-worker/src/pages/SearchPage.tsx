import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';

/** GET /search's results page. The query box itself lives in the header
 * (Layout.tsx, present on every page, pre-filled with the current query via
 * Layout's searchQuery prop) — this page used to render its own second
 * search form too, which read as two separate (and confusingly,
 * differently-filled) search inputs stacked on the page at once. Removed
 * per the user's own report. The query works for either a few keywords or
 * a full question: this is semantic (embedding) search against
 * assistant-worker's KnowledgeBase, not literal keyword matching -- the
 * "Semantic Search" badge + subtitle below call that out explicitly
 * (portfolio value: this is worth surfacing, not just an implementation
 * detail buried in code comments). */
export const SearchPage: FC<{ query: string; articles: Article[]; errorMessage?: string }> = ({
  query,
  articles,
  errorMessage,
}) => (
  <>
    <div class="mb-2 flex items-center gap-3">
      <h1 class="text-2xl font-semibold">Search</h1>
      <span class="rounded-full bg-blue-100 px-3 py-1 text-xs font-medium text-blue-700">
        Semantic Search
      </span>
    </div>
    <p class="mb-8 text-sm text-gray-500">
      Powered by AI embeddings — search by keyword or ask a full question, both work.
    </p>

    {errorMessage && <p class="mb-6 text-sm text-red-600">{errorMessage}</p>}

    {!errorMessage && query !== '' && articles.length === 0 && (
      <p class="text-gray-500">No articles found for "{query}".</p>
    )}

    {!errorMessage && query !== '' && articles.length > 0 && (
      <p class="mb-4 text-sm text-gray-500">Showing results for "{query}"</p>
    )}

    {articles.map((article) => (
      <ArticleCard article={article} />
    ))}
  </>
);
