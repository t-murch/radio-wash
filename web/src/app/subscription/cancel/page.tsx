import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';

import { getMeServer } from '../../services/api';
import { SubscriptionCancelClient } from './subscription-cancel-client';

// Dynamic route: per-request Sentry trace metadata, see subscription/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Subscription canceled',
    robots: { index: false, follow: false },
    other: { ...Sentry.getTraceData() },
  };
}

export default async function SubscriptionCancelPage() {
  const user = await getMeServer();

  return <SubscriptionCancelClient initialUser={user} />;
}
