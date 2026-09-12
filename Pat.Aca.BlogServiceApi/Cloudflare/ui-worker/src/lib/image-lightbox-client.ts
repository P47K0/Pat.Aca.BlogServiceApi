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
 * The popover sizes itself to fit-content the instant showPopover() runs —
 * calling it right after setting .src raced the image's own load (still
 * 0x0 at that instant), so the backdrop appeared but the image never did.
 * decode() resolves once the image actually has pixels (immediately if
 * already cached), so showPopover() only runs once there's real content to
 * size around; .catch() still opens on a broken/failed image rather than
 * silently doing nothing. */
export const IMAGE_LIGHTBOX_CLIENT_SCRIPT = `(function () {
  var content = document.getElementById('article-content');
  var lightbox = document.getElementById('image-lightbox');
  var lightboxImg = document.getElementById('image-lightbox-img');
  if (!content || !lightbox || !lightboxImg) return;

  var images = content.querySelectorAll('img');
  for (var i = 0; i < images.length; i++) {
    images[i].addEventListener('click', function (event) {
      lightboxImg.alt = event.target.alt || '';
      lightboxImg.src = event.target.src;

      var open = function () { lightbox.showPopover(); };
      if (typeof lightboxImg.decode === 'function') {
        lightboxImg.decode().then(open).catch(open);
      } else {
        open();
      }
    });
  }
})();`;
