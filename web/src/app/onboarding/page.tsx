import * as Sentry from '@sentry/nextjs';
import type { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { createClient } from '@/lib/supabase/server';
import {
  getConnectionStatusServer,
  getUserJobsServer,
  type ConnectionStatus,
  type Job,
} from '@/services/api';
import { RouteErrorFallback } from '../components/ux/RouteErrorFallback';
import { SentryErrorBoundary } from '../components/ux/SentryErrorBoundary';
import { OnboardingClient } from './onboarding-client';

// Dynamic route: per-request Sentry trace metadata, see dashboard/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Get started',
    robots: { index: false, follow: false },
    other: { ...Sentry.getTraceData() },
  };
}

/**
 * The guided route from a signed-in account to a first clean playlist.
 *
 * It exists because signing in and granting music access are two separate
 * permissions. Previously the second was a card on an otherwise-empty dashboard,
 * so a new user landed on a full interface with nothing in it and no obvious next
 * action. Here each moment is its own screen and the sequence is visible.
 *
 * Which step to show is decided on the server from real state, so the correct
 * screen renders first rather than flashing the wrong one and correcting itself.
 */
export default async function OnboardingPage() {
  const supabase = await createClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth');
  }

  // A failure here must not strand someone mid-onboarding: treat an unreadable
  // connection as "not connected" and let them try to authorize. The worst case
  // is a redundant Apple prompt, which is far better than a dead end.
  let connection: ConnectionStatus | null = null;
  let jobs: Job[] = [];
  try {
    [connection, jobs] = await Promise.all([
      getConnectionStatusServer('apple_music'),
      getUserJobsServer().catch(() => [] as Job[]),
    ]);
  } catch (error) {
    console.error('Onboarding could not read connection status:', error);
  }

  // Someone who has already cleaned a playlist is not onboarding any more.
  if (connection?.connected && jobs.length > 0) {
    redirect('/dashboard');
  }

  return (
    <SentryErrorBoundary
      fallback={<RouteErrorFallback retryHref="/onboarding" />}
    >
      <OnboardingClient
        email={user.email ?? ''}
        appleConnected={connection?.connected ?? false}
      />
    </SentryErrorBoundary>
  );
}
