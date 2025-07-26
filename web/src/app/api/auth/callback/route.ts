import { createClient } from '@/lib/supabase/server';
import { NextResponse } from 'next/server';

export async function GET(request: Request) {
  const { searchParams, origin } = new URL(request.url);
  const code = searchParams.get('code');
  const spotify = searchParams.get('spotify');
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
      const baseOrigin = origin.includes('localhost') ? 'https://127.0.0.1:3000' : origin;
      
      // If spotify=true parameter is present, redirect to Spotify connection flow
      if (spotify === 'true') {
        console.log('Redirecting to Spotify connection flow');
        return NextResponse.redirect(`${baseOrigin}/auth/success?spotify=true`);
      }
      
      return NextResponse.redirect(`${baseOrigin}${next}`);
    }
    // return the user to an error page with instructions
    return NextResponse.redirect(`${origin}/auth?error=${error}`);
  }
}
