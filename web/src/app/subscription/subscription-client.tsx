'use client';

import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { useSubscriptionStatus, useSubscribeToSync } from '@/hooks/useSubscriptionSync';
import { cancelSubscription, type User } from '../services/api';
import { toast } from 'sonner';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';

const formatDateTime = (dateString: string) => {
  return new Date(dateString).toLocaleString();
};

export function SubscriptionClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const { data: subscriptionStatus, isLoading } = useSubscriptionStatus();
  const subscribeToSyncMutation = useSubscribeToSync();

  const cancelSubscriptionMutation = useMutation<{ success: boolean }, Error>({
    mutationFn: cancelSubscription,
    onSuccess: () => {
      toast.success('Subscription cancelled successfully');
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
    },
    onError: (error) => {
      toast.error('Failed to cancel subscription');
      console.error('Cancel subscription error:', error);
    },
  });

  const handleSubscribe = async () => {
    try {
      await subscribeToSyncMutation.mutateAsync();
      // Note: The mutation will redirect to Stripe checkout on success
    } catch (error) {
      if (error instanceof Error && error.message.includes('404')) {
        toast.error('Subscription service not yet configured. Please contact support.');
      } else {
        toast.error('Subscription failed. Please try again.');
      }
      console.error('Subscribe error:', error);
    }
  };

  const handleCancelSubscription = async () => {
    if (confirm('Are you sure you want to cancel your subscription? This will disable all active sync configurations.')) {
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
          <h1 className="text-3xl font-bold text-foreground">Subscription Management</h1>
          <p className="text-muted-foreground mt-2">
            Manage your RadioWash sync subscription
          </p>
        </div>

        <div className="bg-card border border-border rounded-lg p-6">
          {subscriptionStatus?.hasActiveSubscription ? (
            <div className="space-y-6">
              <div className="flex items-center space-x-3">
                <div className="w-3 h-3 bg-green-500 rounded-full"></div>
                <h2 className="text-xl font-semibold text-foreground">Active Subscription</h2>
              </div>

              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <h3 className="font-medium text-foreground">Plan Details</h3>
                  <div className="text-sm space-y-1">
                    <div>
                      <span className="text-muted-foreground">Plan:</span>
                      <span className="ml-2 font-medium">{subscriptionStatus.planName || 'Sync Plan'}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Status:</span>
                      <span className="ml-2 font-medium text-green-600">{subscriptionStatus.status || 'Active'}</span>
                    </div>
                    {subscriptionStatus.currentPeriodEnd && (
                      <div>
                        <span className="text-muted-foreground">Next billing:</span>
                        <span className="ml-2 font-medium">{formatDateTime(subscriptionStatus.currentPeriodEnd)}</span>
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
                  variant="destructive"
                  onClick={handleCancelSubscription}
                  disabled={cancelSubscriptionMutation.isPending}
                >
                  {cancelSubscriptionMutation.isPending ? 'Cancelling...' : 'Cancel Subscription'}
                </Button>
              </div>
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
                  <div className="w-12 h-12 bg-blue-100 dark:bg-blue-900 rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">⏰</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">Save Time</h3>
                  <p className="text-sm text-muted-foreground">
                    No more checking for changes or running new jobs manually
                  </p>
                </div>
                <div className="text-center">
                  <div className="w-12 h-12 bg-green-100 dark:bg-green-900 rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">🎯</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">Stay Updated</h3>
                  <p className="text-sm text-muted-foreground">
                    Your clean playlists automatically reflect source changes
                  </p>
                </div>
                <div className="text-center">
                  <div className="w-12 h-12 bg-purple-100 dark:bg-purple-900 rounded-full flex items-center justify-center mx-auto mb-3">
                    <span className="text-2xl">🔄</span>
                  </div>
                  <h3 className="font-semibold text-foreground mb-2">Set & Forget</h3>
                  <p className="text-sm text-muted-foreground">
                    Enable once, works forever in the background
                  </p>
                </div>
              </div>

              {/* Pricing Card */}
              <div className="bg-gradient-to-br from-purple-50 to-blue-50 dark:from-purple-950/20 dark:to-blue-950/20 border border-purple-200 dark:border-purple-800 rounded-xl p-8 max-w-md mx-auto">
                <div className="text-center">
                  <div className="flex items-center justify-center mb-4">
                    <span className="text-4xl font-bold text-foreground">$5</span>
                    <span className="text-muted-foreground ml-1">/month</span>
                  </div>
                  <h3 className="text-xl font-semibold text-foreground mb-4">Sync Plan</h3>
                  
                  <div className="space-y-3 text-left mb-6">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Playlists</span>
                      <span className="font-medium text-foreground">Up to 10</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Tracks per playlist</span>
                      <span className="font-medium text-foreground">Up to 200</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Sync frequency</span>
                      <span className="font-medium text-foreground">Daily</span>
                    </div>
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Manual triggering</span>
                      <span className="font-medium text-foreground">✓ Included</span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Features List */}
              <div className="bg-card border border-border rounded-lg p-6 max-w-2xl mx-auto">
                <h3 className="text-lg font-semibold text-foreground mb-4 text-center">Everything Included</h3>
                <div className="grid md:grid-cols-2 gap-3 text-sm">
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Daily automatic synchronization</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Smart track matching & cleaning</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Manual sync triggering</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Sync history & status tracking</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Enable/disable anytime</span>
                  </div>
                  <div className="flex items-center">
                    <span className="text-green-600 mr-2">✓</span>
                    <span>Cancel anytime</span>
                  </div>
                </div>
              </div>

              <Button
                onClick={handleSubscribe}
                disabled={subscribeToSyncMutation.isPending}
                size="lg"
                className="bg-purple-600 hover:bg-purple-700 text-white"
              >
                {subscribeToSyncMutation.isPending ? 'Subscribing...' : 'Subscribe to Sync'}
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