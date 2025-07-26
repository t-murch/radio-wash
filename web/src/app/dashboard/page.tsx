import { createClient } from '@/lib/supabase/server';
import { getMe, getUserPlaylists } from '@/services/api';
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
  const [me, playlists] = await Promise.all([
    getMe(),
    getUserPlaylists(), // User ID is now derived from the JWT on the backend
    // getUserJobs(0), // Still needs user ID parameter - will be updated later
  ]);

  return (
    <DashboardClient
      serverUser={user}
      initialMe={me}
      initialPlaylists={playlists}
      initialJobs={[]}
    />
  );
}
