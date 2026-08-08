'use client';

import Link from 'next/link';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { type User } from '../../services/api';

/**
 * Stripe checkout was abandoned. Deliberately neutral: leaving a checkout is a
 * decision, not an error, and the page's one job is confirming that nothing
 * was charged.
 */
export function SubscriptionCancelClient({
  initialUser,
}: {
  initialUser: User;
}) {
  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/subscription"
        backButtonLabel="Back to Subscription"
      />
      <main className="mx-auto max-w-xl px-4 py-16 sm:px-6 lg:px-8">
        <div className="space-y-8">
          <div className="space-y-3">
            <h1 className="font-display text-3xl font-semibold text-foreground">
              No charge was made
            </h1>
            <p className="text-muted-foreground">
              You left checkout before paying, so Auto-Sync stays off — and
              everything you already use stays free. Cleaning playlists never
              needs a subscription.
            </p>
          </div>

          <div className="flex flex-col gap-3 sm:flex-row">
            <Button asChild>
              <Link href="/subscription">Back to Auto-Sync</Link>
            </Button>
            <Button variant="outline" asChild>
              <Link href="/dashboard">Go to Dashboard</Link>
            </Button>
          </div>
        </div>
      </main>
    </div>
  );
}
