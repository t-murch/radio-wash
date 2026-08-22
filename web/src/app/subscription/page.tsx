import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';

import { getMeServer } from '../services/api';
import { SubscriptionClient } from './subscription-client';

// Dynamic route: per-request Sentry trace metadata, see dashboard/page.tsx.
// noindex belt-and-braces on top of the robots.txt disallow — a disallowed URL
// that earns an external link can otherwise still be indexed URL-only.
export function generateMetadata(): Metadata {
  return {
    title: 'Subscription',
    robots: { index: false, follow: false },
    other: { ...Sentry.getTraceData() },
  };
}

export default async function SubscriptionPage() {
  const user = await getMeServer();

  return <SubscriptionClient initialUser={user} />;
}
