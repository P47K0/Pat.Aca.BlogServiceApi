import type { FC } from 'hono/jsx';
import type { PublicComment } from '../types';
import { formatDate } from '../lib/format-date';

// Mirror blog-service-api's CommentSettings defaults (MaxAuthorNameLength/
// MaxTextLength/MaxEmailLength) — client-side-only, for immediate form
// feedback. The server enforces the real, configurable caps regardless; if
// those are ever tuned away from the current defaults, these drift out of
// sync until updated by hand. Accepted: worst case is a slightly-wrong
// maxlength hint, not a security or correctness issue, since validation
// still happens server-side either way.
const MAX_AUTHOR_NAME_LENGTH = 100;
const MAX_TEXT_LENGTH = 2000;
const MAX_EMAIL_LENGTH = 254;

/** Plain HTML form POST, not a fetch/AJAX submission — matches this site's
 * mostly-no-client-JS approach (the one deliberate exception, Turnstile's
 * own widget script, is unavoidable for any CAPTCHA-like challenge and
 * isn't code this Worker owns). index.tsx's POST handler redirects back to
 * this same article page with a `comment`/`message` query-string result,
 * which this component renders as a banner — see this component's `status`
 * prop. */
export const CommentSection: FC<{
  comments: PublicComment[];
  turnstileSiteKey: string;
  status?: 'success' | 'error';
  message?: string;
}> = ({ comments, turnstileSiteKey, status, message }) => (
  <div class="mt-6 rounded-2xl bg-white p-6 shadow-sm">
    <h2 class="text-lg font-semibold text-gray-900">
      Comments{comments.length > 0 && ` (${comments.length})`}
    </h2>

    {comments.length > 0 && (
      <ul class="mt-4 space-y-4">
        {comments.map((comment) => (
          <li class="border-t border-gray-100 pt-4 first:border-t-0 first:pt-0">
            <p class="text-sm font-medium text-gray-900">
              {comment.authorName}
              <span class="ml-2 font-normal text-gray-500">{formatDate(comment.createdAt)}</span>
            </p>
            <p class="mt-1 whitespace-pre-wrap text-gray-700">{comment.text}</p>
          </li>
        ))}
      </ul>
    )}

    {status === 'success' && (
      <p class="mt-4 rounded-xl bg-green-50 px-4 py-3 text-sm text-green-800">
        Thanks for your comment — it's being reviewed and will appear once approved. Check back later.
      </p>
    )}
    {status === 'error' && (
      <p class="mt-4 rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800">
        {message || 'Something went wrong submitting your comment. Please try again.'}
      </p>
    )}

    <form method="post" class="mt-6 space-y-3">
      <div>
        <label for="authorName" class="block text-sm font-medium text-gray-700">
          Name
        </label>
        <input
          type="text"
          id="authorName"
          name="authorName"
          required
          maxlength={MAX_AUTHOR_NAME_LENGTH}
          class="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none"
        />
      </div>
      <div>
        <label for="email" class="block text-sm font-medium text-gray-700">
          Email <span class="font-normal text-gray-500">(optional, never shown publicly)</span>
        </label>
        <input
          type="email"
          id="email"
          name="email"
          maxlength={MAX_EMAIL_LENGTH}
          class="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none"
        />
      </div>
      <div>
        <label for="text" class="block text-sm font-medium text-gray-700">
          Comment
        </label>
        <textarea
          id="text"
          name="text"
          required
          rows={4}
          maxlength={MAX_TEXT_LENGTH}
          class="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none"
        ></textarea>
      </div>
      {/* Cloudflare's own hosted widget script — the one deliberate
          exception to this site's no-client-JS approach, unavoidable for
          any CAPTCHA-like challenge. Renders the interactive challenge and
          populates a hidden `cf-turnstile-response` field on the form
          automatically; verified server-side in index.tsx's POST handler
          before the comment ever reaches api-proxy. */}
      <script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
      <div class="cf-turnstile" data-sitekey={turnstileSiteKey}></div>
      <button
        type="submit"
        class="rounded-2xl bg-blue-600 px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:bg-blue-700"
      >
        Post comment
      </button>
    </form>
  </div>
);
