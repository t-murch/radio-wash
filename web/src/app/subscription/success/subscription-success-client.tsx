'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import {
  completeCheckout,
  getSubscriptionStatus,
  type User,
} from '../../services/api';
import { useQueryClient } from '@tanstack/react-query';

const POLL_INTERVAL_MS = 2000;
const POLL_TIMEOUT_MS = 30000;

type ActivationState = 'activating' | 'active' | 'delayed';

export function SubscriptionSuccessClient({
  initialUser,
  sessionId,
}: {
  initialUser: User;
  sessionId?: string | null;
}) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [state, setState] = useState<ActivationState>('activating');

  useEffect(() => {
    let cancelled = false;
    let intervalId: ReturnType<typeof setInterval> | undefined;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    const stopTimers = () => {
      if (intervalId) clearInterval(intervalId);
      if (timeoutId) clearTimeout(timeoutId);
    };

    const activate = () => {
      if (cancelled) return;
      stopTimers();
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
      queryClient.invalidateQueries({ queryKey: ['current-subscription'] });
      setState('active');
    };

    // Poll the subscription status until the webhook lands, then give a
    // reassuring "still working on it" message rather than an error — the
    // payment itself has already succeeded.
    const startPolling = () => {
      if (cancelled) return;
      intervalId = setInterval(async () => {
        try {
          const status = await getSubscriptionStatus();
          if (status?.hasActiveSubscription) {
            activate();
          }
        } catch (error) {
          // Transient failure — keep polling until the deadline.
          console.error('Subscription status poll failed:', error);
        }
      }, POLL_INTERVAL_MS);
      timeoutId = setTimeout(() => {
        if (cancelled) return;
        if (intervalId) clearInterval(intervalId);
        setState((current) => (current === 'activating' ? 'delayed' : current));
      }, POLL_TIMEOUT_MS);
    };

    const run = async () => {
      if (!sessionId) {
        // Old bookmark or direct visit — no session to reconcile.
        startPolling();
        return;
      }
      try {
        const status = await completeCheckout(sessionId);
        if (status?.hasActiveSubscription) {
          activate();
        } else {
          startPolling();
        }
      } catch (error) {
        // Reconciliation failed (e.g. session not found yet, or it belongs to
        // an older login). The webhook may still activate the subscription, so
        // fall back to polling instead of alarming the user.
        console.error('Checkout completion failed:', error);
        startPolling();
      }
    };

    run();

    return () => {
      cancelled = true;
      stopTimers();
    };
  }, [sessionId, queryClient]);

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-4xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
        {state === 'activating' && (
          <div className="text-center">
            <div
              className="mx-auto h-12 w-12 rounded-full border-4 border-muted border-t-brand animate-spin mb-4"
              role="status"
              aria-label="Activating"
            ></div>
            <h1 className="text-3xl font-bold text-foreground mb-2">
              Activating your subscription…
            </h1>
            <p className="text-lg text-muted-foreground">
              Hang tight — we&apos;re confirming your payment with Stripe.
            </p>
          </div>
        )}

        {state === 'delayed' && (
          <div className="text-center">
            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-info-muted mb-4">
              <svg
                className="h-6 w-6 text-info"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0"
                />
              </svg>
            </div>
            <h1 className="text-3xl font-bold text-foreground mb-2">
              Payment received
            </h1>
            <p className="text-lg text-muted-foreground mb-8 max-w-xl mx-auto">
              Activation is taking longer than expected. Your subscription will
              appear shortly.
            </p>
            <Button
              onClick={() => router.push('/dashboard')}
              size="lg"
              className="bg-info hover:bg-info-hover text-info-foreground"
            >
              Go to Dashboard
            </Button>
          </div>
        )}

        {state === 'active' && (
          <div className="text-center">
            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-success-muted mb-4">
              <svg
                className="h-6 w-6 text-success"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M5 13l4 4L19 7"
                />
              </svg>
            </div>

            <h1 className="text-3xl font-bold text-foreground mb-2">
              Subscription Successful!
            </h1>

            <p className="text-lg text-muted-foreground mb-8">
              Welcome to RadioWash Sync! You can now enable automatic playlist
              synchronization.
            </p>

            <div className="bg-card border border-border rounded-lg p-6 max-w-md mx-auto mb-8">
              <h2 className="text-lg font-semibold text-foreground mb-4">
                What&apos;s Next?
              </h2>
              <ul className="space-y-2 text-sm text-muted-foreground text-left">
                <li className="flex items-center">
                  <span className="text-success mr-2">✓</span>
                  Complete a playlist cleaning job
                </li>
                <li className="flex items-center">
                  <span className="text-success mr-2">✓</span>
                  Enable sync from the job details page
                </li>
                <li className="flex items-center">
                  <span className="text-success mr-2">✓</span>
                  Manage your sync configurations
                </li>
                <li className="flex items-center">
                  <span className="text-success mr-2">✓</span>
                  Enjoy automatic daily synchronization
                </li>
              </ul>
            </div>

            <div className="flex flex-col sm:flex-row gap-4 justify-center">
              <Button
                onClick={() => router.push('/dashboard')}
                size="lg"
                className="bg-info hover:bg-info-hover text-info-foreground"
              >
                Go to Dashboard
              </Button>
              <Button
                onClick={() => router.push('/subscription')}
                variant="outline"
                size="lg"
              >
                Manage Subscription
              </Button>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
