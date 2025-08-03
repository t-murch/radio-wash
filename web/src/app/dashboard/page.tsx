import { createClient } from '@/lib/supabase/server';
import {
  getMeServer,
  getUserPlaylistsServer,
  getUserJobsServer,
} from '@/services/api';
import { redirect } from 'next/navigation';
import { DashboardClient } from './dashboard-client';

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
