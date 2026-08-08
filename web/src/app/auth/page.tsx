import { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { createClient } from '@/lib/supabase/server';
import { FloatingFeedbackButton } from '../components/ux/ReportBug-Btn';
import { sendMagicLink, signInWithApple, signInWithGoogle } from './actions';
import { AuthForm } from './auth-form';

export const metadata: Metadata = {
  title: 'Sign in',
  description: 'Sign in to RadioWash to make clean copies of your playlists.',
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
    <div className="flex min-h-screen flex-col items-center justify-center bg-background p-6">
      <AuthForm
        sendMagicLink={sendMagicLink}
        signInWithApple={signInWithApple}
        signInWithGoogle={signInWithGoogle}
      />
      <FloatingFeedbackButton />
    </div>
  );
}
