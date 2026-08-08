import { Metadata } from 'next';

import { createClient } from '@/lib/supabase/server';
import LandingPage from './components/ux/LandingPage';
import { StructuredData } from './components/StructuredData';

export const metadata: Metadata = {
  title: 'RadioWash — clean copies of your Apple Music playlists',
  description:
    'Make a clean copy of any Apple Music playlist — same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
  keywords: [
    'Apple Music playlist cleaner',
    'clean version playlist',
    'radio edit playlist',
    'remove explicit tracks',
    'family-friendly Apple Music',
    'clean playlist generator',
  ],
  alternates: {
    canonical: 'https://radiowash.com',
  },
  openGraph: {
    title: 'RadioWash — clean copies of your Apple Music playlists',
    description:
      'Same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
    url: 'https://radiowash.com',
    siteName: 'RadioWash',
    type: 'website',
    locale: 'en_US',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'RadioWash — clean copies of your Apple Music playlists',
    description:
      'Same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
  },
};

export default async function HomePage() {
  // The landing page greets a returning user differently, so it needs to know
  // whether there is a session. getUser() validates against Supabase rather than
  // trusting the cookie's contents.
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();

  return (
    <>
      <StructuredData />
      <LandingPage signedIn={Boolean(user)} email={user?.email ?? undefined} />
    </>
  );
}
