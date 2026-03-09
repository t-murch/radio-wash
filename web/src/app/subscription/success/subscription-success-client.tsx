'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { type User } from '../../services/api';
import { useQueryClient } from '@tanstack/react-query';
import { useVerifyCheckoutSession } from '@/hooks/useSubscriptionSync';

function LoadingState() {
  return (
    <div className="text-center">
      <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-info-muted mb-4 animate-pulse">
        <svg
          className="h-6 w-6 text-info animate-spin"
          fill="none"
          viewBox="0 0 24 24"
        >
          <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
          />
          <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
          />
        </svg>
      </div>
      <h1 className="text-3xl font-bold text-foreground mb-2">
        Verifying Your Subscription...
      </h1>
      <p className="text-lg text-muted-foreground">
        Please wait while we confirm your payment.
      </p>
    </div>
  );
}

function SuccessState({ onDashboard, onManage }: { onDashboard: () => void; onManage: () => void }) {
  return (
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
        Welcome to RadioWash Sync! You can now enable automatic playlist synchronization.
      </p>

      <div className="bg-card border border-border rounded-lg p-6 max-w-md mx-auto mb-8">
        <h2 className="text-lg font-semibold text-foreground mb-4">What&apos;s Next?</h2>
        <ul className="space-y-2 text-sm text-muted-foreground text-left">
          <li className="flex items-center">
            <span className="text-success mr-2">&#10003;</span>
            Complete a playlist cleaning job
          </li>
          <li className="flex items-center">
            <span className="text-success mr-2">&#10003;</span>
            Enable sync from the job details page
          </li>
          <li className="flex items-center">
            <span className="text-success mr-2">&#10003;</span>
            Manage your sync configurations
          </li>
          <li className="flex items-center">
            <span className="text-success mr-2">&#10003;</span>
            Enjoy automatic daily synchronization
          </li>
        </ul>
      </div>

      <div className="flex flex-col sm:flex-row gap-4 justify-center">
        <Button
          onClick={onDashboard}
          size="lg"
          className="bg-info hover:bg-info-hover text-info-foreground"
        >
          Go to Dashboard
        </Button>
        <Button
          onClick={onManage}
          variant="outline"
          size="lg"
        >
          Manage Subscription
        </Button>
      </div>
    </div>
  );
}

function TimeoutState({ onDashboard }: { onDashboard: () => void }) {
  return (
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
            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z"
          />
        </svg>
      </div>

      <h1 className="text-3xl font-bold text-foreground mb-2">
        Could Not Confirm Your Subscription
      </h1>

      <p className="text-lg text-muted-foreground mb-8">
        Your payment may still be processing. Please check your dashboard in a few minutes to
        see your subscription status.
      </p>

      <Button
        onClick={onDashboard}
        size="lg"
        className="bg-info hover:bg-info-hover text-info-foreground"
      >
        Go to Dashboard
      </Button>
    </div>
  );
}

function NoSessionState({ onDashboard }: { onDashboard: () => void }) {
  return (
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
            d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
          />
        </svg>
      </div>

      <h1 className="text-3xl font-bold text-foreground mb-2">
        No Checkout Session Found
      </h1>

      <p className="text-lg text-muted-foreground mb-8">
        It looks like you reached this page without completing checkout. Please check your dashboard
        for your subscription status.
      </p>

      <Button
        onClick={onDashboard}
        size="lg"
        className="bg-info hover:bg-info-hover text-info-foreground"
      >
        Go to Dashboard
      </Button>
    </div>
  );
}

export function SubscriptionSuccessClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const sessionId = searchParams.get('session_id');

  const { isVerified, isLoading, isTimeout } = useVerifyCheckoutSession(sessionId);

  // Invalidate subscription queries when verified
  useEffect(() => {
    if (isVerified) {
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
      queryClient.invalidateQueries({ queryKey: ['current-subscription'] });
    }
  }, [isVerified, queryClient]);

  const handleDashboard = () => router.push('/dashboard');
  const handleManage = () => router.push('/subscription');

  let content: React.ReactNode;

  if (!sessionId) {
    content = <NoSessionState onDashboard={handleDashboard} />;
  } else if (isLoading) {
    content = <LoadingState />;
  } else if (isVerified) {
    content = <SuccessState onDashboard={handleDashboard} onManage={handleManage} />;
  } else if (isTimeout) {
    content = <TimeoutState onDashboard={handleDashboard} />;
  } else {
    content = <LoadingState />;
  }

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-4xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
        {content}
      </main>
    </div>
  );
}
