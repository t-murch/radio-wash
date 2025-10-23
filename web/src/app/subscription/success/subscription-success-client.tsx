'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { type User } from '../../services/api';
import { useQueryClient } from '@tanstack/react-query';

export function SubscriptionSuccessClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();
  const queryClient = useQueryClient();

  useEffect(() => {
    // Invalidate subscription-related queries to refetch updated status
    queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
    queryClient.invalidateQueries({ queryKey: ['current-subscription'] });
  }, [queryClient]);

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-4xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
        <div className="text-center">
          <div className="mx-auto flex items-center justify-center h-12 w-12 rounded-full bg-green-100 mb-4">
            <svg
              className="h-6 w-6 text-green-600"
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
            <h2 className="text-lg font-semibold text-foreground mb-4">What's Next?</h2>
            <ul className="space-y-2 text-sm text-muted-foreground text-left">
              <li className="flex items-center">
                <span className="text-green-600 mr-2">✓</span>
                Complete a playlist cleaning job
              </li>
              <li className="flex items-center">
                <span className="text-green-600 mr-2">✓</span>
                Enable sync from the job details page
              </li>
              <li className="flex items-center">
                <span className="text-green-600 mr-2">✓</span>
                Manage your sync configurations
              </li>
              <li className="flex items-center">
                <span className="text-green-600 mr-2">✓</span>
                Enjoy automatic daily synchronization
              </li>
            </ul>
          </div>

          <div className="flex flex-col sm:flex-row gap-4 justify-center">
            <Button
              onClick={() => router.push('/dashboard')}
              size="lg"
              className="bg-blue-600 hover:bg-blue-700 text-white"
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
      </main>
    </div>
  );
}