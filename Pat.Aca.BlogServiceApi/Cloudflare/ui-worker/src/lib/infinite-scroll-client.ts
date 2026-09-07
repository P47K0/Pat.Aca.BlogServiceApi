/** Vanilla JS, injected verbatim into a <script> tag by Home.tsx — the only
 * client-side JavaScript anywhere on this site. Deliberately not a bundled
 * module: this Worker has no client build step (Tailwind is a CDN `<script>`
 * too), so the source is just a plain string constant embedded as-is.
 *
 * Watches #infinite-scroll-sentinel (present only when the homepage has more
 * than HOMEPAGE_ARTICLE_COUNT articles) with an IntersectionObserver. On
 * intersect, fetches GET /partials/articles?after=<cursor> — a
 * server-rendered HTML fragment (see ArticlesFragment.tsx) — and moves its
 * cards into the real list, then reads the fragment's own sentinel for the
 * next cursor/has-more to decide whether to keep watching.
 *
 * A fetch that fails (offline, upstream error, or a slow/cold-starting
 * origin — infinite scroll's "load more" has no durable-fallback protection,
 * unlike the homepage's initial load) shows a plain retry message rather
 * than silently doing nothing; scrolling back into view tries again since
 * the sentinel is never removed from the DOM on failure. */
export const INFINITE_SCROLL_CLIENT_SCRIPT = `(function () {
  var list = document.getElementById('article-list');
  var sentinel = document.getElementById('infinite-scroll-sentinel');
  if (!list || !sentinel) return;

  var loading = false;

  function setMessage(text) {
    sentinel.textContent = text;
  }

  function loadMore() {
    if (loading || sentinel.dataset.hasMore !== 'true') return;
    loading = true;
    setMessage('Loading more articles…');

    fetch('/partials/articles?after=' + encodeURIComponent(sentinel.dataset.nextCursor || ''))
      .then(function (response) {
        if (!response.ok) throw new Error('load-more request failed');
        return response.text();
      })
      .then(function (html) {
        var temp = document.createElement('div');
        temp.innerHTML = html;

        var cards = temp.querySelector('#article-cards');
        if (cards) {
          while (cards.firstChild) {
            list.insertBefore(cards.firstChild, sentinel);
          }
        }

        var nextSentinel = temp.querySelector('#infinite-scroll-sentinel');
        sentinel.dataset.hasMore = nextSentinel ? nextSentinel.dataset.hasMore : 'false';
        sentinel.dataset.nextCursor = nextSentinel ? nextSentinel.dataset.nextCursor : '';
        setMessage('');
        loading = false;

        if (sentinel.dataset.hasMore !== 'true') {
          observer.disconnect();
        }
      })
      .catch(function () {
        setMessage("Couldn't load more articles. Scroll to try again.");
        loading = false;
      });
  }

  var observer = new IntersectionObserver(function (entries) {
    if (entries.some(function (entry) { return entry.isIntersecting; })) {
      loadMore();
    }
  });
  observer.observe(sentinel);
})();`;
