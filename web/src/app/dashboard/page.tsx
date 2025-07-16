import { createClient } from '@/lib/supabase/server';
import { getMe } from '@/services/api';
import { redirect } from 'next/navigation';
import { DashboardClient } from './dashboard-client';

export default async function DashboardPage() {
  const supabase = await createClient();

  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth');
  }

  // Fetch initial data on the server
  const [me] = await Promise.all([
    getMe(),
    // getUserPlaylists(0), // The user ID is now derived from the JWT on the backend
    // getUserJobs(0), // So we can pass a placeholder here.
  ]);

  return (
    <DashboardClient
      serverUser={user}
      initialMe={me}
      initialPlaylists={[]}
      initialJobs={[]}
    />
  );
}
