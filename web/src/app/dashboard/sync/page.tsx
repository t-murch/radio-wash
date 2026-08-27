import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';

import { getMeServer } from '../../services/api';
import { RouteErrorFallback } from '../../components/ux/RouteErrorFallback';
import { SentryErrorBoundary } from '../../components/ux/SentryErrorBoundary';
import { SyncDashboardClient } from './sync-dashboard-client';

// Dynamic route: per-request Sentry trace metadata, see dashboard/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Auto-Sync',
    robots: { index: false },
    other: { ...Sentry.getTraceData() },
  };
}

export default async function SyncDashboardPage() {
  const user = await getMeServer();

  return (
    <SentryErrorBoundary
      fallback={
        <div className="flex min-h-screen items-center justify-center bg-background p-6">
          <RouteErrorFallback retryHref="/dashboard/sync" />
        </div>
      }
    >
      <SyncDashboardClient initialUser={user} />
    </SentryErrorBoundary>
  );
}