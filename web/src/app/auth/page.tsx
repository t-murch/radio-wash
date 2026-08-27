import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { createClient } from '@/lib/supabase/server';
import { FloatingFeedbackButton } from '../components/ux/ReportBug-Btn';
import { SentryErrorBoundary } from '../components/ux/SentryErrorBoundary';
import { sendMagicLink, signInWithApple, signInWithGoogle } from './actions';
import { AuthForm } from './auth-form';

// Dynamic route: per-request Sentry trace metadata, see dashboard/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Sign in',
    description: 'Sign in to RadioWash to make clean copies of your playlists.',
    robots: {
      index: false,
      follow: false,
    },
    other: { ...Sentry.getTraceData() },
  };
}

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
      {/* Browser extensions and Chrome's page translation can mutate the DOM
          out from under React mid sign-in; contain that here instead of letting
          it unmount the whole app via global-error. */}
      <SentryErrorBoundary
        fallback={
          <div className="w-full max-w-md space-y-4">
            <h1 className="font-display text-2xl font-semibold text-foreground">
              Something went wrong
            </h1>
            <p className="text-muted-foreground">
              <a href="/auth" className="text-primary underline underline-offset-4">
                Reload this page
              </a>{' '}
              to continue signing in.
            </p>
          </div>
        }
      >
        <AuthForm
          sendMagicLink={sendMagicLink}
          signInWithApple={signInWithApple}
          signInWithGoogle={signInWithGoogle}
        />
      </SentryErrorBoundary>
      <FloatingFeedbackButton />
    </div>
  );
}
