import { getMeServer } from '../../services/api';
import { SubscriptionCancelClient } from './subscription-cancel-client';

export default async function SubscriptionCancelPage() {
  const user = await getMeServer();

  return <SubscriptionCancelClient initialUser={user} />;
}