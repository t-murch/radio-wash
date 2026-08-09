import { type EmailOtpType } from '@supabase/supabase-js';
import { NextResponse, type NextRequest } from 'next/server';

import { createClient } from '@/lib/supabase/server';
import { requestOrigin } from '@/lib/request-origin';

/**
 * Magic-link landing route.
 *
 * The PKCE flow verifies a token hash here rather than exchanging a code — see
 * supabase/templates/magic-link.html, which links to this route with
 * {{ .TokenHash }}. A link that is expired, already used, or malformed lands on
 * /auth/link-expired, which offers a fresh one.
 */
export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  // Redirect on the host the browser is on. Deriving the origin from
  // request.url sends dev users from 127.0.0.1 to localhost, stranding the
  // just-set session cookie on the wrong host.
  const origin = requestOrigin(request);
  const tokenHash = searchParams.get('token_hash');
  const type = searchParams.get('type') as EmailOtpType | null;

  const next = safeNext(searchParams.get('next'));

  if (!tokenHash || !type) {
    return NextResponse.redirect(
      `${origin}/auth/link-expired?reason=malformed`
    );
  }

  const supabase = await createClient();
  const { error } = await supabase.auth.verifyOtp({
    type,
    token_hash: tokenHash,
  });

  if (error) {
    // Supabase does not distinguish "already used" from "expired" — both are the
    // same failure to the user, and the screen says so rather than guessing.
    return NextResponse.redirect(`${origin}/auth/link-expired`);
  }

  return NextResponse.redirect(`${origin}${next}`);
}

/**
 * Only same-origin relative paths are honoured. An attacker-supplied absolute
 * URL here would turn the sign-in link into an open redirect, landing a freshly
 * authenticated user on a page of someone else's choosing.
 */
function safeNext(candidate: string | null): string {
  const fallback = '/onboarding';
  if (!candidate) return fallback;
  if (!candidate.startsWith('/') || candidate.startsWith('//')) return fallback;
  return candidate;
}
