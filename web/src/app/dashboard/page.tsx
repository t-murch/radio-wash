import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { createClient } from '@/lib/supabase/server';
import {
  getMeServer,
  getUserPlaylistsServer,
  getUserJobsServer,
} from '@/services/api';
import { DashboardClient } from './dashboard-client';

// This route is dynamic (auth cookies), so the Sentry trace metadata is fresh
// per request — it moved here from the root layout when the static marketing
// pages stopped being able to carry request-scoped values.
export function generateMetadata(): Metadata {
  return {
    title: 'Dashboard',
    robots: { index: false },
    other: { ...Sentry.getTraceData() },
  };
}

export default async function DashboardPage() {
  const supabase = await createClient();

  const {
    data: { user },
    error,
  } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth');
  }

  // Fetch initial data on the server
  const [me, playlists, jobs] = await Promise.all([
    getMeServer(),
    getUserPlaylistsServer(), // User ID is now derived from the JWT on the backend
    getUserJobsServer(), // User ID is now derived from the JWT on the backend
  ]);

  return (
    <DashboardClient
      serverUser={user}
      initialMe={me}
      initialPlaylists={playlists}
      initialJobs={jobs}
    />
  );
}
