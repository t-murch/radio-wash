import { updateSession } from '@/lib/supabase/middleware';
import { type NextRequest } from 'next/server';

export async function middleware(request: NextRequest) {
  return updateSession(request);
}

export const config = {
  matcher: [
    /*
     * Match all request paths except:
     * - _next/static, _next/image, favicon.ico, static image assets (this
     *   also covers the static icon.png/apple-icon.png metadata files and
     *   the JSON-LD logo under /logo_assets)
     * - robots.txt and sitemap.xml (crawler infrastructure)
     * - the static marketing pages: the root path (the leading `$`
     *   alternative), privacy, terms, how-it-works, guides. These render
     *   statically and must not pay a Supabase round-trip per request.
     *   Session cookies still refresh on every authed route.
     * The page prefixes are anchored with (?:/|$) so a future route that
     * merely starts with one of these names still runs the middleware.
     */
    '/((?!$|_next/static|_next/image|favicon.ico|robots.txt|sitemap.xml|privacy(?:/|$)|terms(?:/|$)|how-it-works(?:/|$)|guides(?:/|$)|.*\\.(?:svg|png|jpg|jpeg|gif|webp)$).*)',
  ],
};
