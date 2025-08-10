import { createClient } from '@/lib/supabase/server';
import { NextResponse } from 'next/server';

export async function GET(request: Request) {
  const { searchParams, origin } = new URL(request.url);
  const code = searchParams.get('code');
  const platform = searchParams.get('platform');
  // if "next" is in param, use it as the redirect URL
  const next = searchParams.get('next') ?? '/dashboard';
  console.log(`callback origin: ${origin}`);

  console.log(`next: ${next}`);

  if (code) {
    const supabase = await createClient();
    const { error } = await supabase.auth.exchangeCodeForSession(code);

    console.log(`auth/callback error: ${JSON.stringify(error)}`);
    console.log(`auth/callback origin: ${JSON.stringify(origin)}`);

    if (!error) {
      const baseOrigin =
        process.env.NODE_ENV === 'development'
          ? 'https://127.0.0.1:3000'
          : process.env.NEXT_PUBLIC_WEB_URL;

      const {
        data: { session },
      } = await supabase.auth.getSession();

      if (
        session?.provider_token &&
        session?.provider_refresh_token &&
        platform === 'spotify'
      ) {
        try {
          const apiUrl =
            process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159';
          await fetch(`${apiUrl}/api/auth/spotify/tokens`, {
            method: 'POST',
            headers: {
              Authorization: `Bearer ${session.access_token}`,
              'Content-Type': 'application/json',
            },
            body: JSON.stringify({
              accessToken: session.provider_token,
              refreshToken: session.provider_refresh_token,
              expiresAt: new Date(Date.now() + 3600 * 1000).toISOString(),
            }),
          });
          console.log('Successfully synced Spotify tokens to API');
        } catch (tokenSyncError) {
          console.error('Failed to sync Spotify tokens:', tokenSyncError);
        }
      }

      if (platform === 'spotify') {
        console.log('Redirecting to Spotify connection flow');
        return NextResponse.redirect(`${baseOrigin}${next}`);
      } else if (platform === 'apple') {
        console.log('Redirecting to Apple connection flow');
      }

      return NextResponse.redirect(`${baseOrigin}${next}`);
    }
    // return the user to an error page with instructions
    const baseOrigin =
      process.env.NODE_ENV === 'development'
        ? 'https://127.0.0.1:3000'
        : process.env.NEXT_PUBLIC_WEB_URL;
    return NextResponse.redirect(`${baseOrigin}/auth?error=${error}`);
  }
}
