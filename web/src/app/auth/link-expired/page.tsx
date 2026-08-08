import type { Metadata } from 'next';

import { sendMagicLink, signInWithApple, signInWithGoogle } from '../actions';
import { LinkExpired } from './link-expired-client';

export const metadata: Metadata = {
  title: 'Sign-in link expired',
  robots: { index: false, follow: false },
};

/**
 * One screen for two failures. Supabase does not distinguish an already-used
 * link from an expired one, and to the person holding it the difference does not
 * matter — a fresh link fixes both.
 *
 * Note this screen deliberately drops the stepper: the user is not partway
 * through the sequence here, they have fallen out of it, and showing progress
 * against a step they cannot currently complete would be misleading.
 */
export default function LinkExpiredPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-6">
      <LinkExpired
        sendMagicLink={sendMagicLink}
        signInWithApple={signInWithApple}
        signInWithGoogle={signInWithGoogle}
      />
    </div>
  );
}
