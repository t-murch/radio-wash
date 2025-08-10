import { createClient } from '@/lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';
import { AuthForm } from './auth-form';

export default async function LoginPage() {
  const supabase = await createClient();

  const {
    data: { user },
    error,
  } = await supabase.auth.getUser();

  if (user) {
    redirect('/dashboard');
  }

  const signInWithPlatform = async (
    platform: 'spotify' | 'apple' = 'spotify'
  ) => {
    'use server';
    const supabase = await createClient();
    const headerList = await headers();
    const origin = headerList.get('origin');

    console.log(`auth/page origin: ${origin}`);

    // If connectSpotify is true, add spotify=true parameter to callback
    const callbackUrl = `${origin}/api/auth/callback?platform=${platform}`;

    console.log(`auth/page redirectTo: ${callbackUrl}`);

    const { data, error } = await supabase.auth.signInWithOAuth({
      provider: platform,
      options: {
        scopes:
          'user-read-email playlist-read-private playlist-modify-private playlist-modify-public',
        redirectTo: callbackUrl,
      },
    });

    if (data.url) {
      return redirect(data.url);
    }
    if (error) {
      return redirect('/auth?error=Could not authenticate user');
    }
  };

  const signInWithSpotify = async () => {
    'use server';
    return signInWithPlatform('spotify');
  };

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-background p-4">
      <AuthForm signInWithSpotify={signInWithSpotify} />
    </div>
  );
}
