import { getMeServer } from '../../services/api';
import { SubscriptionSuccessClient } from './subscription-success-client';

export default async function SubscriptionSuccessPage() {
  const user = await getMeServer();

  return <SubscriptionSuccessClient initialUser={user} />;
}