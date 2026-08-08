'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import {
  ApiError,
  completeCheckout,
  getSubscriptionStatus,
  type User,
} from '../../services/api';
import { useQueryClient } from '@tanstack/react-query';

const POLL_INTERVAL_MS = 2000;
const POLL_TIMEOUT_MS = 30000;
const DELAYED_POLL_INTERVAL_MS = 10000;
// An activation that hasn't confirmed by now isn't going to confirm from this
// tab — stop polling instead of hitting the API forever from abandoned tabs.
const MAX_POLL_DURATION_MS = 10 * 60 * 1000;

type ActivationState =
  | 'checking' // no session id — verifying the current status, no payment claims
  | 'activating' // fresh checkout session being reconciled
  | 'active'
  | 'delayed'
  | 'unverified' // the checkout session was rejected (403/404)
  | 'stalled'; // polling hit the hard cap without confirmation

// 403/404 mean the session doesn't exist or belongs to another user —
// retrying cannot succeed, and no payment can be claimed.
const isSessionRejection = (error: unknown) =>
  error instanceof ApiError && (error.status === 403 || error.status === 404);

// A 401 means the session expired — no amount of polling can succeed until
// the user signs in again.
const isAuthExpiry = (error: unknown) =>
  error instanceof ApiError && error.status === 401;

export function SubscriptionSuccessClient({
  initialUser,
  sessionId,
}: {
  initialUser: User;
  sessionId?: string | null;
}) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [state, setState] = useState<ActivationState>(
    sessionId ? 'activating' : 'checking'
  );

  useEffect(() => {
    let cancelled = false;
    let stopped = false;
    let pollTimerId: ReturnType<typeof setTimeout> | undefined;
    let deadlineId: ReturnType<typeof setTimeout> | undefined;
    let hardStopId: ReturnType<typeof setTimeout> | undefined;
    let delayed = false;
    let needsReconcile = false;
    let tick = 0;

    const stopTimers = () => {
      if (pollTimerId) clearTimeout(pollTimerId);
      if (deadlineId) clearTimeout(deadlineId);
      if (hardStopId) clearTimeout(hardStopId);
    };

    const activate = () => {
      if (cancelled) return;
      stopTimers();
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
      queryClient.invalidateQueries({ queryKey: ['current-subscription'] });
      setState('active');
    };

    // One status request at a time: each completed request schedules the
    // next via setTimeout, so slow responses never stack.
    const scheduleNextPoll = () => {
      if (cancelled || stopped) return;
      pollTimerId = setTimeout(
        pollOnce,
        delayed ? DELAYED_POLL_INTERVAL_MS : POLL_INTERVAL_MS
      );
    };

    const pollOnce = async () => {
      if (cancelled) return;
      tick += 1;
      // checkout/complete is idempotent and designed for retry — after a
      // transient failure, re-attempt it on every other tick instead of
      // relying solely on GET /status.
      const reconcileThisTick = needsReconcile && !!sessionId && tick % 2 === 0;
      try {
        let status;
        if (reconcileThisTick && sessionId) {
          status = await completeCheckout(sessionId);
          needsReconcile = false;
        } else {
          status = await getSubscriptionStatus();
        }
        if (cancelled) return;
        if (status?.hasActiveSubscription) {
          activate();
          return;
        }
      } catch (error) {
        if (cancelled) return;
        if (reconcileThisTick && isSessionRejection(error)) {
          stopTimers();
          setState('unverified');
          return;
        }
        if (isAuthExpiry(error)) {
          stopTimers();
          router.replace('/auth');
          return;
        }
        // Transient failure — keep polling.
        console.error('Subscription status poll failed:', error);
      }
      scheduleNextPoll();
    };

    // Poll the subscription status until the webhook lands. After the
    // deadline, switch to the slower "delayed" cadence but keep checking —
    // until the hard cap, after which the page stops and says so.
    const startPolling = () => {
      if (cancelled) return;
      deadlineId = setTimeout(() => {
        if (cancelled) return;
        delayed = true;
        setState((current) => (current === 'activating' ? 'delayed' : current));
      }, POLL_TIMEOUT_MS);
      hardStopId = setTimeout(() => {
        if (cancelled) return;
        stopped = true;
        stopTimers();
        setState((current) =>
          current === 'activating' || current === 'delayed'
            ? 'stalled'
            : current
        );
      }, MAX_POLL_DURATION_MS);
      scheduleNextPoll();
    };

    const run = async () => {
      if (!sessionId) {
        // Old bookmark or direct visit — nothing to reconcile and no payment
        // to claim. A subscribed user shouldn't be bounced by one transient
        // failure, so retry the check once before redirecting.
        for (let attempt = 0; attempt < 2; attempt++) {
          try {
            const status = await getSubscriptionStatus();
            if (cancelled) return;
            if (status?.hasActiveSubscription) {
              activate();
            } else {
              router.replace('/subscription');
            }
            return;
          } catch (error) {
            if (cancelled) return;
            if (isAuthExpiry(error)) {
              router.replace('/auth');
              return;
            }
            console.error('Subscription status check failed:', error);
            if (attempt === 0) {
              await new Promise((resolve) =>
                setTimeout(resolve, POLL_INTERVAL_MS)
              );
              if (cancelled) return;
            }
          }
        }
        router.replace('/subscription');
        return;
      }
      try {
        const status = await completeCheckout(sessionId);
        if (cancelled) return;
        if (status?.hasActiveSubscription) {
          activate();
        } else {
          // Reconciled, but the webhook hasn't landed yet — poll GET /status.
          startPolling();
        }
      } catch (error) {
        if (cancelled) return;
        if (isSessionRejection(error)) {
          // The session was rejected outright — don't claim a payment
          // happened, and don't poll on its behalf.
          setState('unverified');
          return;
        }
        if (isAuthExpiry(error)) {
          router.replace('/auth');
          return;
        }
        // Transient reconciliation failure (5xx / network). The endpoint is
        // idempotent, so retry it during the poll window.
        console.error('Checkout completion failed:', error);
        needsReconcile = true;
        startPolling();
      }
    };

    run();

    return () => {
      cancelled = true;
      stopTimers();
    };
  }, [sessionId, queryClient, router]);

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-4xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
        {state === 'checking' && (
          <div className="text-center">
            <div
              className="mx-auto h-12 w-12 rounded-full border-4 border-muted border-t-primary animate-spin mb-4"
              role="status"
              aria-label="Checking"
            ></div>
            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
              Checking subscription status…
            </h1>
            <p className="text-lg text-muted-foreground">
              One moment while we look up your subscription.
            </p>
          </div>
        )}

        {state === 'activating' && (
          <div className="text-center">
            <div
              className="mx-auto h-12 w-12 rounded-full border-4 border-muted border-t-primary animate-spin mb-4"
              role="status"
              aria-label="Activating"
            ></div>
            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
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
            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
              Still activating…
            </h1>
            <p className="text-lg text-muted-foreground mb-8 max-w-xl mx-auto">
              Activation is taking longer than expected. This page keeps
              checking automatically — you can also head to the dashboard and
              come back later.
            </p>
            <Button onClick={() => router.push('/dashboard')} size="lg">
              Go to Dashboard
            </Button>
          </div>
        )}

        {state === 'stalled' && (
          <div className="text-center">
            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-warning-muted mb-4">
              <svg
                className="h-6 w-6 text-warning"
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
            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
              Activation still pending
            </h1>
            <p className="text-lg text-muted-foreground mb-8 max-w-xl mx-auto">
              We&apos;ve stopped checking automatically. Your payment may still
              be processing — check the subscription page in a few minutes, and
              contact support if your subscription doesn&apos;t appear.
            </p>
            <Button onClick={() => router.push('/subscription')} size="lg">
              Go to Subscription
            </Button>
          </div>
        )}

        {state === 'unverified' && (
          <div className="text-center">
            <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-warning-muted mb-4">
              <svg
                className="h-6 w-6 text-warning"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth="2"
                  d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z"
                />
              </svg>
            </div>
            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
              We couldn&apos;t verify this checkout session
            </h1>
            <p className="text-lg text-muted-foreground mb-8 max-w-xl mx-auto">
              This link may be outdated or belong to a different account. Check
              your subscription status on the subscription page.
            </p>
            <Button onClick={() => router.push('/subscription')} size="lg">
              Go to Subscription
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

            <h1 className="font-display text-3xl font-semibold text-foreground mb-2">
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
              <Button onClick={() => router.push('/dashboard')} size="lg">
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
