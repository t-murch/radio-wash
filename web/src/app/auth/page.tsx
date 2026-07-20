import { createClient } from '@/lib/supabase/server';
import { redirect } from 'next/navigation';
import { Metadata } from 'next';
import { AuthForm } from './auth-form';
import { FloatingFeedbackButton } from '../components/ux/ReportBug-Btn';
import { signInWithApple, signInWithSpotify } from './actions';

export const metadata: Metadata = {
  title: 'Sign In',
  description: 'Sign in to RadioWash with your Spotify or Apple account',
  robots: {
    index: false,
    follow: false,
  },
};

export default async function LoginPage() {
  const supabase = await createClient();

  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (user) {
    redirect('/dashboard');
  }

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-background p-4">
      <AuthForm
        signInWithSpotify={signInWithSpotify}
        signInWithApple={signInWithApple}
      />
      <FloatingFeedbackButton />
    </div>
  );
}
