/** Verifies a submitted Turnstile response token server-side via
 * Cloudflare's own siteverify endpoint — same call as ui-worker's
 * verifyTurnstile (see that file), duplicated rather than shared per this
 * repo's own "no shared code between sibling Workers" convention (see
 * README.md). remoteIp is optional extra validation (Cloudflare cross-checks
 * it against the token) — passed as the same CF-Connecting-IP value already
 * used for this Worker's rate limiter.
 *
 * Fails closed: any error talking to Cloudflare (network failure,
 * unexpected response shape) is treated as verification failure, not
 * silently allowed through — same reasoning as ui-worker's copy, this is a
 * bot-defense layer in front of the rate limiter and real Workers AI/Cosmos
 * spend, so an outage here should block the request, not bypass it.
 *
 * Stubbed in ahead of the chat widget itself (Phase 7): nothing can supply a
 * real token until that widget renders the Turnstile challenge client-side,
 * so this can't be exercised end-to-end yet — see handleAsk's own comment. */
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
