'use server';

import { createClient } from '@/lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';

const signInWithPlatform = async (
  platform: 'spotify' | 'apple' = 'spotify'
) => {
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
    redirect(data.url);
  }
  if (error) {
    redirect('/auth?error=Could not authenticate user');
  }
};

export const signInWithSpotify = async () => {
  return signInWithPlatform('spotify');
};