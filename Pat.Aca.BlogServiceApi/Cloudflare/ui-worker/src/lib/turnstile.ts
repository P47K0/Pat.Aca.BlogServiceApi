/** Verifies a submitted Turnstile response token server-side via
 * Cloudflare's own siteverify endpoint — the widget itself (rendered
 * client-side, see CommentSection.tsx) only proves a browser solved the
 * challenge; this call is what actually confirms that with Cloudflare
 * before the comment is allowed through to api-proxy at all. remoteIp is
 * optional extra validation (Cloudflare cross-checks it against the token)
 * — passed as the same CF-Connecting-IP-derived value used for the
 * X-Real-Client-Ip forwarding elsewhere in this request.
 *
 * Fails closed: any error talking to Cloudflare (network failure,
 * unexpected response shape) is treated as verification failure, not
 * silently allowed through — this is the one bot-defense layer in front
 * of the rate limiter and the LLM moderation check, so an outage here
 * should block submission, not bypass it. */
export async function verifyTurnstile(secretKey: string, token: string, remoteIp: string): Promise<boolean> {
  if (!token) {
    return false;
  }

  try {
    const body = new URLSearchParams({ secret: secretKey, response: token });
    if (remoteIp) {
      body.set('remoteip', remoteIp);
    }

    const response = await fetch('https://challenges.cloudflare.com/turnstile/v0/siteverify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body,
    });

    if (!response.ok) {
      return false;
    }

    const result = (await response.json()) as { success?: boolean };
    return result.success === true;
  } catch {
    return false;
  }
}
