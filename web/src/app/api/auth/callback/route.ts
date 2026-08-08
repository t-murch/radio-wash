import { NextResponse } from 'next/server';

import { createClient } from '@/lib/supabase/server';

/**
 * OAuth callback for the identity providers (Apple, Google).
 *
 * Note what this route no longer does: it used to forward `provider_token` to
 * the API to register a music connection. That was a Spotify-shaped assumption.
 * Neither identity provider yields music access — Apple Music requires a
 * separate MusicKit authorization, which onboarding handles as an explicit step.
 */
export async function GET(request: Request) {
  const { searchParams, origin } = new URL(request.url);
  const code = searchParams.get('code');
  const next = safeNext(searchParams.get('next'));

  if (!code) {
    return NextResponse.redirect(
      `${origin}/auth?error=${encodeURIComponent(
        'That sign-in link was incomplete. Please try again.'
      )}`
    );
  }

  const supabase = await createClient();
  const { error } = await supabase.auth.exchangeCodeForSession(code);

  if (error) {
    console.error('OAuth code exchange failed:', error);
    return NextResponse.redirect(
      `${origin}/auth?error=${encodeURIComponent(
        'We could not complete that sign-in. Please try again.'
      )}`
    );
  }

  // `origin` comes from the incoming request, so this works in development,
  // preview deployments, and production without a hardcoded host. Behind a load
  // balancer the forwarded host is the externally visible one.
  const forwardedHost = request.headers.get('x-forwarded-host');
  const isLocal = process.env.NODE_ENV === 'development';

  if (!isLocal && forwardedHost) {
    return NextResponse.redirect(`https://${forwardedHost}${next}`);
  }

  return NextResponse.redirect(`${origin}${next}`);
}

/**
 * Only same-origin relative paths are honoured, so a crafted `next` cannot turn
 * the callback into an open redirect for a freshly authenticated session.
 */
function safeNext(candidate: string | null): string {
  const fallback = '/onboarding';
  if (!candidate) return fallback;
  if (!candidate.startsWith('/') || candidate.startsWith('//')) return fallback;
  return candidate;
}
