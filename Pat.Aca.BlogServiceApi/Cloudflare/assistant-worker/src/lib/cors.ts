// POST /ask is called directly from the browser (the chat widget's
// client-side JS, on a different origin than this Worker's own
// ai-assistant.koorevaar.com), so every response needs CORS headers or the
// browser drops it before the widget's JS ever sees it -- including error
// responses (429/403/400/500), not just the 200 path, since a CORS-blocked
// error response just looks like a generic network failure to fetch(),
// hiding the real status from the widget entirely.
const ALLOWED_METHODS = 'POST, OPTIONS';
const ALLOWED_HEADERS = 'Content-Type';

function corsHeaders(allowedOrigin: string): HeadersInit {
  return {
    'Access-Control-Allow-Origin': allowedOrigin,
    'Access-Control-Allow-Methods': ALLOWED_METHODS,
    'Access-Control-Allow-Headers': ALLOWED_HEADERS,
    // A day is Cloudflare's own documented max for this header -- just
    // cuts down on repeat preflight OPTIONS requests, not a security knob.
    'Access-Control-Max-Age': '86400',
  };
}

/** Mutates `response`'s headers to add CORS ones and returns it -- Response
 * objects built via Response.json()/new Response() have mutable headers
 * up until the response is actually sent, so this avoids constructing a
 * whole new Response just to attach a few headers. */
export function withCors(response: Response, allowedOrigin: string): Response {
  const headers = corsHeaders(allowedOrigin);
  for (const [name, value] of Object.entries(headers)) {
    response.headers.set(name, value);
  }
  return response;
}

/** The browser's own CORS preflight for POST /ask (triggered by the
 * `Content-Type: application/json` header, which makes this a "non-simple"
 * request) -- a bare 204 with the same CORS headers, no body, before the
 * real request is even sent. */
export function handlePreflight(allowedOrigin: string): Response {
  return new Response(null, { status: 204, headers: corsHeaders(allowedOrigin) });
}
