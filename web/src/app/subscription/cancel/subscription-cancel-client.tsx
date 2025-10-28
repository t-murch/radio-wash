'use client';

import { useRouter } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { type User } from '../../services/api';

export function SubscriptionCancelClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/subscription"
        backButtonLabel="Back to Subscription"
      />
      <main className="max-w-4xl mx-auto py-12 px-4 sm:px-6 lg:px-8">
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
                d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.732 16.5c-.77.833.192 2.5 1.732 2.5z"
              />
            </svg>
          </div>
          
          <h1 className="text-3xl font-bold text-foreground mb-2">
            Subscription Cancelled
          </h1>
          
          <p className="text-lg text-muted-foreground mb-8">
            You cancelled the subscription process. No charges were made.
          </p>

          <div className="bg-card border border-border rounded-lg p-6 max-w-md mx-auto mb-8">
            <h2 className="text-lg font-semibold text-foreground mb-4">Still Interested?</h2>
            <p className="text-sm text-muted-foreground mb-4">
              You can always subscribe later to enable automatic playlist synchronization. 
              The sync feature helps keep your clean playlists up to date automatically.
            </p>
          </div>

          <div className="flex flex-col sm:flex-row gap-4 justify-center">
            <Button
              onClick={() => router.push('/subscription')}
              size="lg"
              className="bg-brand hover:bg-brand-hover text-brand-foreground"
            >
              Try Again
            </Button>
            <Button
              onClick={() => router.push('/dashboard')}
              variant="outline"
              size="lg"
            >
              Back to Dashboard
            </Button>
          </div>
        </div>
      </main>
    </div>
  );
}