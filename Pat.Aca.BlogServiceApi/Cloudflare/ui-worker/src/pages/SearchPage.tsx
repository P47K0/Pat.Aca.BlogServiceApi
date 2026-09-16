import type { FC } from 'hono/jsx';
import type { Article } from '../types';
import { ArticleCard } from '../components/ArticleCard';

/** GET /search's results page. A plain <form method="get"> — no client-side
 * JS, full page load per submission, matching this site's existing
 * no-client-JS-except-infinite-scroll minimalism. The query box works for
 * either a few keywords or a full question: this is semantic (embedding)
 * search against assistant-worker's KnowledgeBase, not literal keyword
 * matching -- the "Semantic Search" badge + subtitle below call that out
 * explicitly (portfolio value: this is worth surfacing, not just an
 * implementation detail buried in code comments). */
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
    <p class="mb-6 text-sm text-gray-500">
      Powered by AI embeddings — search by keyword or ask a full question, both work.
    </p>
    <form method="get" action="/search" class="mb-8 flex gap-2">
      <input
        type="search"
        name="q"
        value={query}
        placeholder="Search articles… (a topic or a full question both work)"
        class="flex-1 rounded-xl border border-gray-300 px-4 py-2.5 text-gray-900 shadow-sm focus:border-blue-500 focus:outline-none"
      />
      <button
        type="submit"
        class="rounded-xl bg-blue-600 px-5 py-2.5 font-medium text-white shadow-sm transition hover:bg-blue-700"
      >
        Search
      </button>
    </form>

    {errorMessage && <p class="mb-6 text-sm text-red-600">{errorMessage}</p>}

    {!errorMessage && query !== '' && articles.length === 0 && (
      <p class="text-gray-500">No articles found for "{query}".</p>
    )}

    {articles.map((article) => (
      <ArticleCard article={article} />
    ))}
  </>
);
