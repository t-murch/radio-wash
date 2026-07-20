'use server';

import { createClient } from '@/lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';

const SPOTIFY_SCOPES =
  'user-read-email playlist-read-private playlist-modify-private playlist-modify-public';
// Apple sign-in is identity-only; Apple Music access is granted separately via MusicKit
// from the dashboard (Supabase's Apple provider can never yield a Music User Token).
const APPLE_SCOPES = 'name email';

const signInWithPlatform = async (
  platform: 'spotify' | 'apple' = 'spotify'
) => {
  const supabase = await createClient();
  const headerList = await headers();
  const origin = headerList.get('origin');

  const callbackUrl = `${origin}/api/auth/callback?platform=${platform}`;

  const { data, error } = await supabase.auth.signInWithOAuth({
    provider: platform,
    options: {
      scopes: platform === 'apple' ? APPLE_SCOPES : SPOTIFY_SCOPES,
      redirectTo: callbackUrl,
    },
  });

  if (data.url) {
    redirect(data.url);
  }
  if (error) {
    redirect('/auth?error=Could not authenticate user');
  }
};

export const signInWithSpotify = async () => {
  return signInWithPlatform('spotify');
};

export const signInWithApple = async () => {
  return signInWithPlatform('apple');
};
