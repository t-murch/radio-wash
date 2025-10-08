import { getMeServer } from '../../services/api';
import { SyncDashboardClient } from './sync-dashboard-client';

export default async function SyncDashboardPage() {
  const user = await getMeServer();

  return <SyncDashboardClient initialUser={user} />;
}