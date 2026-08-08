'use server';

import { createClient } from '@/lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';

/**
 * Identity providers. None of these grant music access: Apple Music needs a
 * separate MusicKit authorization producing a Music User Token, which onboarding
 * walks the user through as an explicit second step. Google has no music
 * relationship at all.
 */
type IdentityProvider = 'apple' | 'google';

// Apple sign-in is identity-only; Supabase's Apple provider can never yield a
// Music User Token no matter what is requested here.
const APPLE_SCOPES = 'name email';
const GOOGLE_SCOPES = 'email profile';

const SCOPES: Record<IdentityProvider, string> = {
  apple: APPLE_SCOPES,
  google: GOOGLE_SCOPES,
};

async function resolveOrigin() {
  const headerList = await headers();
  return headerList.get('origin') ?? process.env.NEXT_PUBLIC_WEB_URL ?? '';
}

const signInWithProvider = async (provider: IdentityProvider) => {
  const supabase = await createClient();
  const origin = await resolveOrigin();

  const { data, error } = await supabase.auth.signInWithOAuth({
    provider,
    options: {
      scopes: SCOPES[provider],
      redirectTo: `${origin}/api/auth/callback`,
    },
  });

  if (error) {
    redirect('/auth?error=Could not sign you in. Please try again.');
  }

  if (data.url) {
    redirect(data.url);
  }
};

export const signInWithApple = async () => signInWithProvider('apple');
export const signInWithGoogle = async () => signInWithProvider('google');

export type MagicLinkState = {
  status: 'idle' | 'sent' | 'error';
  email?: string;
  message?: string;
};

/**
 * Sends a sign-in link. Deliberately reports success even when Supabase reports
 * that the address does not exist — telling an anonymous caller which email
 * addresses have accounts is an enumeration oracle. The user sees the same
 * check-your-inbox screen either way.
 *
 * Rate-limit errors are the exception: those are surfaced, because silently
 * claiming to have sent an email that was throttled leaves someone waiting for
 * a message that will never arrive.
 */
export async function sendMagicLink(
  _prevState: MagicLinkState,
  formData: FormData
): Promise<MagicLinkState> {
  const email = String(formData.get('email') ?? '')
    .trim()
    .toLowerCase();

  if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    return {
      status: 'error',
      email,
      message: "That doesn't look like an email address. Check it and retry.",
    };
  }

  const supabase = await createClient();
  const origin = await resolveOrigin();

  const { error } = await supabase.auth.signInWithOtp({
    email,
    options: {
      emailRedirectTo: `${origin}/auth/confirm`,
      shouldCreateUser: true,
    },
  });

  if (error) {
    if (error.status === 429) {
      return {
        status: 'error',
        email,
        message: 'Too many requests just now. Wait a moment and try again.',
      };
    }

    console.error('Magic link request failed:', error);
    return {
      status: 'error',
      email,
      message: "We couldn't send that link. Try again in a moment.",
    };
  }

  return { status: 'sent', email };
}
