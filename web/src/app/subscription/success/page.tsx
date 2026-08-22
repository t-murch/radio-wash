import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';

import { getMeServer } from '../../services/api';
import { SubscriptionSuccessClient } from './subscription-success-client';

// Dynamic route: per-request Sentry trace metadata, see subscription/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Subscription confirmed',
    robots: { index: false, follow: false },
    other: { ...Sentry.getTraceData() },
  };
}

export default async function SubscriptionSuccessPage({
  searchParams,
}: {
  searchParams: Promise<{ session_id?: string }>;
}) {
  const user = await getMeServer();
  const { session_id: sessionId } = await searchParams;

  return (
    <SubscriptionSuccessClient
      initialUser={user}
      sessionId={sessionId ?? null}
    />
  );
}
