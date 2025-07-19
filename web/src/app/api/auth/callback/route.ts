import { createClient } from '@/lib/supabase/server';
import { NextResponse } from 'next/server';

export async function GET(request: Request) {
  const { searchParams, origin } = new URL(request.url);
  const code = searchParams.get('code');
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
      return NextResponse.redirect(
        `${
          origin.includes('localhost') ? 'https://127.0.0.1:3000' : origin
        }${next}`
      );
    }
    // return the user to an error page with instructions
    return NextResponse.redirect(`${origin}/auth?error=${error}`);
  }
}
