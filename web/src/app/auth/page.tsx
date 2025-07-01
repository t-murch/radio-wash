import { AuthForm } from '@/components/ux/AuthForm';
import { createClient } from '@/lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';

export default async function LoginPage() {
  const supabase = createClient();

  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (user) {
    redirect('/dashboard');
  }

  const signInWithSpotify = async () => {
    'use server';
    const supabase = createClient();
    const origin = (await headers()).get('origin');

    const { data, error } = await supabase.auth.signInWithOAuth({
      provider: 'spotify',
      options: {
        scopes:
          'user-read-email playlist-read-private playlist-modify-private playlist-modify-public',
        redirectTo: `${origin}/auth/callback`,
      },
    });

    if (data.url) {
      return redirect(data.url);
    }
    if (error) {
      return redirect('/auth?error=Could not authenticate user');
    }
  };

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-50 p-4">
      <AuthForm signInWithSpotify={signInWithSpotify} />
    </div>
  );
}
