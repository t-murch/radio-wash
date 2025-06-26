'use client';

import { useEffect, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../hooks/useAuth';

function AuthCallback() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const searchParams = useSearchParams();
  const { requiresMusicServiceSetup, connectedServices } = useAuth();

  useEffect(() => {
    // When this page loads, the browser has received the redirect from the backend,
    // and the 'rw-auth-token' cookie should be set.

    // We invalidate the 'me' query, which triggers a refetch of the user's data.
    // The `getMe` function in `api.ts` will now be called with the cookie attached.
    queryClient.invalidateQueries({ queryKey: ['me'] }).then(() => {
      // Check if this is a callback from music service connection
      const spotifyConnected = searchParams.get('spotify_connected');
      const appleConnected = searchParams.get('apple_connected');
      const hasError = searchParams.get('error');

      if (hasError) {
        // Handle connection errors - redirect to onboarding with error message
        router.push(`/onboarding?error=${hasError}`);
        return;
      }

      if (spotifyConnected || appleConnected) {
        // Refresh connected services after successful connection
        queryClient.invalidateQueries({ queryKey: ['connectedServices'] }).then(() => {
          // Check if user still needs to set up more services or can proceed to dashboard
          const hasAnyServices = connectedServices.length > 0;
          router.push(hasAnyServices ? '/dashboard' : '/onboarding');
        });
        return;
      }

      // For regular auth (signup/signin), check if user needs music service setup
      if (requiresMusicServiceSetup || connectedServices.length === 0) {
        router.push('/onboarding');
      } else {
        router.push('/dashboard');
      }
    });
  }, [queryClient, router, requiresMusicServiceSetup, connectedServices, searchParams]);

  return (
    <div className="flex items-center justify-center min-h-screen bg-gray-100 dark:bg-gray-900">
      <div className="text-center p-8">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-green-500 mx-auto"></div>
        <h1 className="text-2xl font-semibold mt-4 text-gray-800 dark:text-gray-200">
          Finalizing authentication...
        </h1>
        <p className="text-gray-600 dark:text-gray-400 mt-2">
          Please wait while we securely log you in.
        </p>
      </div>
    </div>
  );
}

// Wrap the component in Suspense for good practice, especially if it were to fetch data directly.
export default function AuthCallbackPage() {
  return (
    <Suspense>
      <AuthCallback />
    </Suspense>
  );
}
