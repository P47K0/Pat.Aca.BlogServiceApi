import type { FC } from 'hono/jsx';
import type { Article, PublicComment } from '../types';
import type { SeriesNav } from '../lib/related-articles';
import { TagList } from '../components/TagList';
import { CommentSection } from '../components/CommentSection';
import { formatDate } from '../lib/format-date';
import { IMAGE_LIGHTBOX_CLIENT_SCRIPT } from '../lib/image-lightbox-client';

export const ArticleDetailPage: FC<{
  article: Article;
  seriesNav: SeriesNav | null;
  relatedArticles: Article[];
  comments: PublicComment[];
  turnstileSiteKey: string;
  commentStatus?: 'success' | 'error';
  commentMessage?: string;
}> = ({ article, seriesNav, relatedArticles, comments, turnstileSiteKey, commentStatus, commentMessage }) => {
  // marked (api-proxy) only ever emits lowercase `<img` tags, so a plain
  // substring check is enough to skip the lightbox markup/script entirely
  // on the (common) articles that have no images.
  const hasContentImages = article.content.includes('<img');

  return (
  <>
    <article class="rounded-2xl bg-white p-8 shadow-sm">
      <h1 class="text-3xl font-bold text-gray-900">{article.title}</h1>
      <p class="mt-2 text-sm text-gray-500">
        {formatDate(article.publishedAt)}
        {' · '}
        {article.viewCount} {article.viewCount === 1 ? 'view' : 'views'}
      </p>
      {article.tags.length > 0 && (
        <div class="mt-3">
          <TagList tags={article.tags} />
        </div>
      )}
      {seriesNav && (
        <div class="mt-4 rounded-xl bg-gray-50 px-4 py-3 text-sm text-gray-600">
          <p>
            Part {seriesNav.position} of {seriesNav.total} in <span class="font-medium">{seriesNav.seriesName}</span>
          </p>
          {(seriesNav.prev || seriesNav.next) && (
            <p class="mt-1 flex justify-between gap-4">
              <span>
                {seriesNav.prev && (
                  <a href={`/articles/${seriesNav.prev.slug}`} class="text-blue-700 hover:text-blue-600">
                    ← {seriesNav.prev.title}
                  </a>
                )}
              </span>
              <span class="text-right">
                {seriesNav.next && (
                  <a href={`/articles/${seriesNav.next.slug}`} class="text-blue-700 hover:text-blue-600">
                    {seriesNav.next.title} →
                  </a>
                )}
              </span>
            </p>
          )}
        </div>
      )}
      {article.linkedinVideoEmbedUrl && (
        // LinkedIn's own "Embed video only" iframe — a deliberate dependency
        // on that specific LinkedIn post staying up/Public (breaks silently
        // if the user ever deletes it), accepted for the much lower effort
        // vs. downloading and re-hosting each video the way article images
        // are. LinkedIn's default generated code is a fixed 504x399px box;
        // wrapped in an aspect-ratio container here instead so it scales
        // with this site's responsive layout rather than a hardcoded size.
        <div class="mt-6 aspect-[504/399] w-full max-w-md overflow-hidden rounded-2xl">
          <iframe
            src={article.linkedinVideoEmbedUrl}
            class="h-full w-full"
            frameborder="0"
            allowfullscreen
            title={`${article.title} — video demo`}
          ></iframe>
        </div>
      )}
      {/* `content` is HTML rendered server-side by api-proxy from Markdown that
          the user hand-authors directly in Cosmos (never user-submitted input)
          — see blog-service-api-project memory's auth-architecture note.
          Trusted content, so injecting it directly is an accepted trade-off,
          not an oversight. */}
      <div
        id="article-content"
        class="prose prose-neutral mt-8 max-w-none"
        dangerouslySetInnerHTML={{ __html: article.content }}
      />
      {/* Shared lightbox for every image in the content above. Native
          <popover> handles open/close/light-dismiss/Escape for free, same
          as TagCloud's "Show all tags" popover — the only bit that can't be
          done with a plain popovertarget attribute is wiring the click on
          each (dynamic, server-rendered) <img>, which is what
          image-lightbox-client.ts's small script does. */}
      {hasContentImages && (
        <>
          <div
            id="image-lightbox"
            popover="auto"
            class="m-auto max-h-[90vh] max-w-[90vw] border-0 bg-transparent p-0 backdrop:bg-black/70"
          >
            <div class="relative">
              <button
                popovertarget="image-lightbox"
                popovertargetaction="hide"
                aria-label="Close"
                class="absolute -top-3 -right-3 flex h-8 w-8 items-center justify-center rounded-full bg-white text-lg leading-none text-gray-600 shadow-sm hover:text-gray-900"
              >
                &#x2715;
              </button>
              <img
                id="image-lightbox-img"
                src=""
                alt=""
                class="max-h-[90vh] max-w-[90vw] rounded-lg object-contain"
              />
            </div>
          </div>
          <script dangerouslySetInnerHTML={{ __html: IMAGE_LIGHTBOX_CLIENT_SCRIPT }} />
        </>
      )}
    </article>
    {relatedArticles.length > 0 && (
      <div class="mt-6 rounded-2xl bg-white p-6 shadow-sm">
        <h2 class="text-sm font-semibold text-gray-500">Related articles</h2>
        <ul class="mt-3 space-y-2">
          {relatedArticles.map((related) => (
            <li>
              <a
                href={`/articles/${related.slug}`}
                class="text-gray-900 transition hover:text-blue-600"
              >
                {related.title}
              </a>
              <span class="ml-2 text-sm text-gray-500">{formatDate(related.publishedAt)}</span>
            </li>
          ))}
        </ul>
      </div>
    )}
    <CommentSection
      articleSlug={article.slug}
      comments={comments}
      turnstileSiteKey={turnstileSiteKey}
      status={commentStatus}
      message={commentMessage}
    />
    <a href="/" class="mt-6 block text-sm text-gray-500 transition hover:text-blue-600">
      ← All articles
    </a>
  </>
  );
};
