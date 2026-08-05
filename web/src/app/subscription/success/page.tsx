import { getMeServer } from '../../services/api';
import { SubscriptionSuccessClient } from './subscription-success-client';

export default async function SubscriptionSuccessPage({
  searchParams,
}: {
  searchParams: Promise<{ session_id?: string }>;
}) {
  const user = await getMeServer();
  const { session_id: sessionId } = await searchParams;

  return (
    <SubscriptionSuccessClient initialUser={user} sessionId={sessionId ?? null} />
  );
}
