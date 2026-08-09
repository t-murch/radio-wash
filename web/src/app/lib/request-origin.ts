/**
 * The origin the user's browser actually requested.
 *
 * `new URL(request.url).origin` is not that: Next's dev server rewrites the
 * URL's host to `localhost`, so a user on https://127.0.0.1:3000 who signs in
 * gets redirected across hosts — and the freshly set, host-scoped auth cookie
 * does not follow, which reads as "the magic link didn't sign me in". Behind a
 * load balancer the URL host is the internal one for the same reason. The
 * forwarded/Host headers carry the host the browser addressed.
 */
export function requestOrigin(request: Request): string {
  const url = new URL(request.url);

  const forwardedHost = request.headers.get('x-forwarded-host');
  if (forwardedHost) {
    // A proxy that forwards the host without the proto is assumed to be
    // terminating TLS — https is the only correct guess for a deployed origin.
    const proto = request.headers.get('x-forwarded-proto') ?? 'https';
    return `${proto}://${forwardedHost}`;
  }

  const host = request.headers.get('host');
  if (host) {
    return `${url.protocol}//${host}`;
  }

  return url.origin;
}
