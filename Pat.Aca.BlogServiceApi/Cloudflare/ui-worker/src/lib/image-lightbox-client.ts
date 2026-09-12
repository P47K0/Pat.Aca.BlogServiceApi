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
 * Two earlier attempts at getting the image to actually show (rather than
 * just the backdrop) tried to time this click handler around the image's
 * own load — both wrong. The real fix was sizing: the popover/img in
 * ArticleDetail.tsx now get a definite size independent of the clicked
 * image's own intrinsic dimensions (some article diagrams are SVGs with
 * only a viewBox — an aspect ratio, no intrinsic pixel size, which a
 * fit-content-sized popover can't size around), so plain sequential
 * assignment here is all that's needed.
 *
 * That fixed-size box plus object-contain means an image whose aspect
 * ratio doesn't match the box leaves transparent letterbox space above/
 * below (or beside) it — still technically inside the popover element, so
 * the browser's own light-dismiss (which only fires for clicks outside the
 * popover) never catches a tap there. Closes on any click inside the
 * popover that isn't on the image itself or the close button, so that
 * space is closeable too — the close button can end up unreachable after
 * a mobile orientation change, and this doesn't depend on finding it. */
export const IMAGE_LIGHTBOX_CLIENT_SCRIPT = `(function () {
  var content = document.getElementById('article-content');
  var lightbox = document.getElementById('image-lightbox');
  var lightboxImg = document.getElementById('image-lightbox-img');
  if (!content || !lightbox || !lightboxImg) return;

  var images = content.querySelectorAll('img');
  for (var i = 0; i < images.length; i++) {
    images[i].addEventListener('click', function (event) {
      lightboxImg.src = event.target.src;
      lightboxImg.alt = event.target.alt || '';
      lightbox.showPopover();
    });
  }

  lightbox.addEventListener('click', function (event) {
    if (event.target === lightboxImg || event.target.closest('button')) return;
    lightbox.hidePopover();
  });
})();`;
