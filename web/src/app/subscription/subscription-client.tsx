'use client';

import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import {
  useSubscriptionStatus,
  useSubscribeToSync,
} from '@/hooks/useSubscriptionSync';
import {
  ApiError,
  cancelSubscription,
  createPortalSession,
  type User,
} from '../services/api';
import { toast } from 'sonner';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { CURRENT_PLAN } from '@/lib/constants/pricing';
import {
  trackBillingPortalRequested,
  trackSubscriptionCancellationScheduled,
  trackSubscriptionCheckoutRequested,
} from '@/lib/analytics';

const formatDateTime = (dateString: string) => {
  return new Date(dateString).toLocaleString();
};

export function SubscriptionClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const { data: subscriptionStatus, isLoading } = useSubscriptionStatus();
  const subscribeToSyncMutation = useSubscribeToSync();

  const cancelSubscriptionMutation = useMutation<
    { message: string; activeUntil?: string; cancelAtPeriodEnd?: boolean },
    Error
  >({
    mutationFn: cancelSubscription,
    onSuccess: (data) => {
      trackSubscriptionCancellationScheduled();
      // Cancellation happens at period end — access continues until then.
      toast.success(
        data.activeUntil
          ? `Subscription will cancel on ${formatDateTime(data.activeUntil)}`
          : 'Subscription will cancel at the end of the billing period'
      );
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
    },
    onError: (error) => {
      toast.error('Failed to cancel subscription');
      console.error('Cancel subscription error:', error);
    },
  });

  const portalMutation = useMutation<{ portalUrl?: string }, Error>({
    mutationFn: createPortalSession,
    retry: false,
    onSuccess: (data) => {
      if (data?.portalUrl) {
        trackBillingPortalRequested();
        window.location.href = data.portalUrl;
      } else {
        toast.error('Could not open the billing portal. Please try again.');
      }
    },
    onError: (error) => {
      toast.error('Could not open the billing portal. Please try again.');
      console.error('Portal session error:', error);
    },
  });

  const handleSubscribe = async () => {
    try {
      trackSubscriptionCheckoutRequested();
      await subscribeToSyncMutation.mutateAsync();
      // Note: The mutation will redirect to Stripe checkout on success
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.status === 409) {
          toast.error('You already have an active subscription');
          queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
        } else if (error.status === 503) {
          toast.error(
            'Subscriptions are temporarily unavailable — please try again later'
          );
        } else if (error.status === 429) {
          // The rate limiter responds without a Problem Details body, so the
          // generic detail fallback would show a raw message here.
          toast.error('Too many attempts — please wait a minute and try again');
        } else {
          toast.error(error.detail ?? 'Subscription failed. Please try again.');
        }
      } else {
        toast.error('Subscription failed. Please try again.');
      }
      console.error('Subscribe error:', error);
    }
  };

  const handleCancelSubscription = async () => {
    const accessUntil = subscriptionStatus?.currentPeriodEnd
      ? ` You'll keep access until ${formatDateTime(
          subscriptionStatus.currentPeriodEnd
        )}.`
      : '';
    if (confirm(`Cancel your subscription?${accessUntil}`)) {
      await cancelSubscriptionMutation.mutateAsync();
    }
  };

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background">
        <GlobalHeader
          user={initialUser}
          showBackButton={true}
          backButtonHref="/dashboard"
          backButtonLabel="Back to Dashboard"
        />
        <main className="max-w-4xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="text-center">Loading subscription status...</div>
        </main>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-4xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <div className="mb-6">
          <h1 className="font-display text-3xl font-semibold text-foreground">
            Auto-Sync
          </h1>
          <p className="text-muted-foreground mt-2">
            The one paid feature: keeping your clean copies current.
          </p>
        </div>

        <div className="bg-card border border-border rounded-lg p-6">
          {subscriptionStatus?.hasActiveSubscription ? (
            <div className="space-y-6">
              <div className="flex items-center space-x-3">
                <div className="w-2.5 h-2.5 bg-success rounded-full"></div>
                <h2 className="font-display text-xl font-semibold text-foreground">
                  Active Subscription
                </h2>
              </div>

              {subscriptionStatus.cancelAtPeriodEnd && (
                <div className="bg-warning-muted text-warning rounded-lg p-4 text-sm font-medium">
                  Cancellation scheduled
                  {subscriptionStatus.currentPeriodEnd &&
                    ` — active until ${formatDateTime(
                      subscriptionStatus.currentPeriodEnd
                    )}`}
                </div>
              )}

              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <h3 className="font-medium text-foreground">Plan Details</h3>
                  <div className="text-sm space-y-1">
                    {subscriptionStatus.planName && (
                      <div>
                        <span className="text-muted-foreground">Plan:</span>
                        <span className="ml-2 font-medium">
                          {subscriptionStatus.planName}
                        </span>
                      </div>
                    )}
                    {subscriptionStatus.status && (
                      <div>
                        <span className="text-muted-foreground">Status:</span>
                        <span className="ml-2 font-medium text-success">
                          {subscriptionStatus.status}
                        </span>
                      </div>
                    )}
                    {subscriptionStatus.currentPeriodEnd && (
                      <div>
                        <span className="text-muted-foreground">
                          {subscriptionStatus.cancelAtPeriodEnd
                            ? 'Access until:'
                            : 'Next billing:'}
                        </span>
                        <span className="ml-2 font-medium">
                          {formatDateTime(subscriptionStatus.currentPeriodEnd)}
                        </span>
                      </div>
                    )}
                  </div>
                </div>

                <div className="space-y-2">
                  <h3 className="font-medium text-foreground">Features</h3>
                  <ul className="text-sm space-y-1 text-muted-foreground">
                    <li>
                      Daily sync — new songs get their clean versions added
                    </li>
                    <li>
                      Up to {CURRENT_PLAN.FEATURES.MAX_PLAYLISTS} synced
                      playlists
                    </li>
                    <li>Manual sync anytime</li>
                    <li>Sync history and status</li>
                  </ul>
                </div>
              </div>

              <div className="flex flex-wrap gap-3">
                <Button
                  variant="outline"
                  onClick={() => router.push('/dashboard/sync')}
                >
                  View Sync Dashboard
                </Button>
                <Button
                  variant="outline"
                  onClick={() => router.push('/dashboard')}
                >
                  Back to Dashboard
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => portalMutation.mutate()}
                  disabled={portalMutation.isPending}
                >
                  {portalMutation.isPending ? 'Opening...' : 'Manage billing'}
                </Button>
                {!subscriptionStatus.cancelAtPeriodEnd && (
                  <Button
                    variant="destructive"
                    onClick={handleCancelSubscription}
                    disabled={cancelSubscriptionMutation.isPending}
                  >
                    {cancelSubscriptionMutation.isPending
                      ? 'Cancelling...'
                      : 'Cancel Subscription'}
                  </Button>
                )}
              </div>

              {subscriptionStatus.cancelAtPeriodEnd && (
                <p className="text-sm text-muted-foreground">
                  Changed your mind? You can resume billing from the billing
                  portal via &quot;Manage billing&quot;.
                </p>
              )}
            </div>
          ) : (
            <div className="mx-auto max-w-xl space-y-8">
              <div className="space-y-3">
                <h2 className="font-display text-2xl font-semibold text-foreground">
                  A clean copy is a snapshot of the day you made it
                </h2>
                <p className="text-muted-foreground">
                  Auto-Sync checks your source playlist daily and adds the clean
                  versions of any new songs, so the copy keeps up without you
                  re-running jobs.
                </p>
                <p className="text-sm text-muted-foreground">
                  It only ever adds — nothing is removed from your copy.
                </p>
              </div>

              <div className="rounded-md border border-border bg-background p-6">
                <div className="flex items-baseline justify-between">
                  <h3 className="font-display text-lg font-semibold text-foreground">
                    Sync Plan
                  </h3>
                  <p className="text-foreground">
                    <span className="tabular text-2xl font-semibold">
                      {CURRENT_PLAN.MARKETING_PRICE}
                    </span>
                    <span className="text-sm text-muted-foreground">
                      /month
                    </span>
                  </p>
                </div>
                <dl className="mt-4 space-y-2 border-t border-border pt-4 text-sm">
                  <div className="flex justify-between">
                    <dt className="text-muted-foreground">Synced playlists</dt>
                    <dd className="font-medium text-foreground">
                      Up to {CURRENT_PLAN.FEATURES.MAX_PLAYLISTS}
                    </dd>
                  </div>
                  <div className="flex justify-between">
                    <dt className="text-muted-foreground">
                      Checks for new songs
                    </dt>
                    <dd className="font-medium text-foreground">Daily</dd>
                  </div>
                  <div className="flex justify-between">
                    <dt className="text-muted-foreground">Manual sync</dt>
                    <dd className="font-medium text-foreground">Included</dd>
                  </div>
                  <div className="flex justify-between">
                    <dt className="text-muted-foreground">Cancel</dt>
                    <dd className="font-medium text-foreground">Anytime</dd>
                  </div>
                </dl>
              </div>

              <div className="space-y-3">
                <Button
                  onClick={handleSubscribe}
                  disabled={subscribeToSyncMutation.isPending}
                  size="lg"
                >
                  {subscribeToSyncMutation.isPending
                    ? 'Subscribing...'
                    : 'Subscribe to Sync'}
                </Button>
                <p className="text-sm text-muted-foreground">
                  Cleaning playlists stays free either way. You can cancel any
                  time from this page.
                </p>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
