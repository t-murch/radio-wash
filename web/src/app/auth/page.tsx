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

  console.log(`auth/page error: ${JSON.stringify(error)}`);

  if (user) {
    redirect('/dashboard');
  }

  const signInWithSpotify = async () => {
    'use server';
    const supabase = await createClient();
    const headerList = await headers();
    const origin = headerList.get('origin');
    
    console.log(`auth/page origin: ${origin}`);
    console.log(`auth/page redirectTo: ${origin}/api/auth/callback`);

    const { data, error } = await supabase.auth.signInWithOAuth({
      provider: 'spotify',
      options: {
        scopes:
          'user-read-email playlist-read-private playlist-modify-private playlist-modify-public',
        redirectTo: `${origin}/api/auth/callback`,
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
