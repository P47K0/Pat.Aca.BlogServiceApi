/** Vanilla JS, injected verbatim into a <script> tag by ArticleDetail.tsx —
 * see infinite-scroll-client.ts for why this Worker embeds plain script
 * strings like this rather than a bundled client module.
 *
 * Wires a click handler onto every <img> inside #article-content (the
 * server-rendered Markdown body — arbitrary images this component doesn't
 * control the markup of, so this can't be done with a plain `popovertarget`
 * attribute the way TagCloud's "Show all tags" popover is). Clicking an
 * image copies its src/alt into the shared #image-lightbox popover's <img>
 * and opens it; open/close/light-dismiss/Escape all come from the native
 * popover itself, same as TagCloud.
 *
 * Order matters here: showPopover() runs *before* .src is set, so the img
 * is already part of the visible top layer once it starts loading — it
 * then reflows on load exactly like any normal in-page image. Doing it the
 * other way around (set .src, then open) raced the image's own load, and
 * an earlier attempt at fixing that with img.decode() before opening
 * turned out to hang indefinitely for at least one SVG (decode() needs a
 * layout to decode into, which a hidden/not-yet-shown popover doesn't have
 * — this ordering sidesteps that entirely instead of working around it). */
export const IMAGE_LIGHTBOX_CLIENT_SCRIPT = `(function () {
  var content = document.getElementById('article-content');
  var lightbox = document.getElementById('image-lightbox');
  var lightboxImg = document.getElementById('image-lightbox-img');
  if (!content || !lightbox || !lightboxImg) return;

  var images = content.querySelectorAll('img');
  for (var i = 0; i < images.length; i++) {
    images[i].addEventListener('click', function (event) {
      lightboxImg.alt = event.target.alt || '';
      lightbox.showPopover();
      lightboxImg.src = event.target.src;
    });
  }
})();`;
