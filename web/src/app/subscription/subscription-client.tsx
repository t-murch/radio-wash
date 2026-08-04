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
        } else if (error.status === 404) {
          toast.error(
            'Subscription service not yet configured. Please contact support.'
          );
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
          <h1 className="text-3xl font-bold text-foreground">
            Subscription Management
          </h1>
          <p className="text-muted-foreground mt-2">
            Manage your RadioWash sync subscription
          </p>
        </div>

        <div className="bg-card border border-border rounded-lg p-6">
          {subscriptionStatus?.hasActiveSubscription ? (
            <div className="space-y-6">
              <div className="flex items-center space-x-3">
                <div className="w-3 h-3 bg-success rounded-full"></div>
                <h2 className="text-xl font-semibold text-foreground">
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
                    <li>✓ Automatic daily playlist synchronization</li>
                    <li>✓ Unlimited playlist sync configurations</li>
                    <li>✓ Manual sync triggering</li>
                    <li>✓ Sync history and status tracking</li>
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
            <div className="text-center space-y-8">
              <div>
                <h2 className="text-3xl font-bold text-foreground mb-3">
                  Never Manually Update Playlists Again
                </h2>
                <p className="text-lg text-muted-foreground max-w-2xl mx-auto">
                  Your source playlists change. Your clean playlists should too.
                  Auto-Sync keeps everything updated automatically.
                </p>
              </div>

              {/* Value Proposition */}
              <div className="grid md:grid-cols-3 gap-6 max-w-4xl mx-auto">
                <div className="text-center">
                  <div className="w-12 h-12 bg-info-muted rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">⏰</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">
                    Save Time
                  </h3>
                  <p className="text-sm text-muted-foreground">
                    No more checking for changes or running new jobs manually
                  </p>
                </div>
                <div className="text-center">
                  <div className="w-12 h-12 bg-success-muted rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">🎯</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">
                    Stay Updated
                  </h3>
                  <p className="text-sm text-muted-foreground">
                    Your clean playlists automatically reflect source changes
                  </p>
                </div>
                <div className="text-center">
                  <div className="w-12 h-12 bg-brand/20 rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">🔄</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">
                    Set & Forget
                  </h3>
                  <p className="text-sm text-muted-foreground">
                    Enable once, works forever in the background
                  </p>
                </div>
              </div>

              {/* Pricing Card */}
              <div className="bg-gradient-to-br from-brand/10 to-info/10 border border-brand/50 rounded-xl p-8 max-w-md mx-auto">
                <div className="text-center">
                  <div className="flex items-center justify-center mb-4">
                    <span className="text-4xl font-bold text-foreground">
                      {CURRENT_PLAN.MARKETING_PRICE}
                    </span>
                    <span className="text-muted-foreground ml-1">/month</span>
                  </div>
                  <h3 className="text-xl font-semibold text-foreground mb-4">
                    Sync Plan
                  </h3>

                  <div className="space-y-3 text-left mb-6">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Playlists</span>
                      <span className="font-medium text-foreground">
                        Up to 10
                      </span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">
                        Tracks per playlist
                      </span>
                      <span className="font-medium text-foreground">
                        Up to 200
                      </span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">
                        Sync frequency
                      </span>
                      <span className="font-medium text-foreground">Daily</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">
                        Manual triggering
                      </span>
                      <span className="font-medium text-foreground">
                        ✓ Included
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Features List */}
              <div className="bg-card border border-border rounded-lg p-6 max-w-2xl mx-auto">
                <h3 className="text-lg font-semibold text-foreground mb-4 text-center">
                  Everything Included
                </h3>
                <div className="grid md:grid-cols-2 gap-3 text-sm">
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Daily automatic synchronization</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Smart track matching & cleaning</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Manual sync triggering</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Sync history & status tracking</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Enable/disable anytime</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-success mr-2">✓</span>
                    <span>Cancel anytime</span>
                  </div>
                </div>
              </div>

              <Button
                onClick={handleSubscribe}
                disabled={subscribeToSyncMutation.isPending}
                size="lg"
                className="bg-brand hover:bg-brand-hover text-brand-foreground"
              >
                {subscribeToSyncMutation.isPending
                  ? 'Subscribing...'
                  : 'Subscribe to Sync'}
              </Button>

              <p className="text-xs text-muted-foreground">
                You can cancel your subscription at any time from this page
              </p>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

