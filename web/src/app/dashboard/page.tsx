import { createClient } from '@/lib/supabase/server';
import { redirect } from 'next/navigation';
import { DashboardClient } from './dashboard-client';
import { getMe, getUserPlaylists, getUserJobs } from '@/services/api';

export default async function DashboardPage() {
  const supabase = await createClient();

  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth');
  }

  // Fetch initial data on the server
  const [me, playlists, jobs] = await Promise.all([
    getMe(),
    getUserPlaylists(0), // The user ID is now derived from the JWT on the backend
    getUserJobs(0), // So we can pass a placeholder here.
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
