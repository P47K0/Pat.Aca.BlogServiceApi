/** Vanilla JS, injected verbatim into a <script data-slug="..."> tag by
 * ArticleDetail.tsx — see infinite-scroll-client.ts for why this Worker
 * embeds plain script strings like this rather than a bundled client module.
 *
 * Counts one article view (POST /api/view, see lib/referrer-analytics.ts)
 * per page load, once the page is actually shown: a prerendered or
 * background-loaded tab counts only when someone switches to it. Sends the
 * referring page's host only (document.referrer; the beacon's own Referer
 * header is this page) and utm_source from the address. */
export const VIEW_BEACON_CLIENT_SCRIPT = `
(function () {
  var script = document.currentScript;
  var slug = script && script.dataset.slug;
  if (!slug || !navigator.sendBeacon) return;

  var ref = '';
  try {
    if (document.referrer) {
      var url = new URL(document.referrer);
      ref = url.hostname || url.protocol.replace(':', '');
    }
  } catch (e) {}
  var utm = new URLSearchParams(location.search).get('utm_source') || '';

  var sent = false;
  function send() {
    if (sent || document.visibilityState !== 'visible') return;
    sent = true;
    document.removeEventListener('visibilitychange', send);
    navigator.sendBeacon('/api/view', new URLSearchParams({ slug: slug, ref: ref, utm: utm }));
  }
  send();
  document.addEventListener('visibilitychange', send);
})();
`;
