import { createClient } from '@/lib/supabase/server';
import { redirect } from 'next/navigation';
import { AuthForm } from './auth-form';
import { signInWithSpotify } from './actions';

export default async function LoginPage() {
  const supabase = await createClient();

  const {
    data: { user },
    error,
  } = await supabase.auth.getUser();

  if (user) {
    redirect('/dashboard');
  }


  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-background p-4">
      <AuthForm signInWithSpotify={signInWithSpotify} />
    </div>
  );
}
