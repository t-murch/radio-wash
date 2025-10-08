import { getMeServer } from '../services/api';
import { SubscriptionClient } from './subscription-client';

export default async function SubscriptionPage() {
  const user = await getMeServer();

  return <SubscriptionClient initialUser={user} />;
}