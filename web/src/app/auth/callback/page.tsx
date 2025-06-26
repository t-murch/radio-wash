'use client';

import { useEffect, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { createBrowserClient } from '@supabase/ssr';

function AuthCallback() {
  const router = useRouter();
  const searchParams = useSearchParams();

  useEffect(() => {
    const handleAuth = async () => {
      const supabase = createBrowserClient(
        process.env.NEXT_PUBLIC_SUPABASE_URL!,
        process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY!
      );

      // Check if we have tokens in the URL hash (from OAuth flow)
      const hashParams = new URLSearchParams(window.location.hash.substring(1));
      const accessToken = hashParams.get('access_token');
      const refreshToken = hashParams.get('refresh_token');

      if (accessToken && refreshToken) {
        // Set the session from the tokens provided by the backend
        const { data, error } = await supabase.auth.setSession({
          access_token: accessToken,
          refresh_token: refreshToken,
        });

        if (error) {
          console.error('Auth callback error:', error);
          router.replace('/auth?error=true');
          return;
        }

        if (data.session) {
          // Clear the hash from the URL for security
          window.history.replaceState(null, '', window.location.pathname);
          router.replace('/dashboard');
          return;
        }
      }

      // Check if we have an email param (simplified flow)
      const email = searchParams.get('email');
      if (email) {
        // For now, just show success - in production you'd handle this better
        console.log('Auth successful for:', email);
        // TODO: Implement proper session handling for simplified flow
        router.replace('/dashboard');
        return;
      }

      // If no tokens or email, check if user is already authenticated
      const { data: { session } } = await supabase.auth.getSession();
      
      if (session) {
        router.replace('/dashboard');
      } else {
        router.replace('/auth?error=no_session');
      }
    };

    handleAuth();
  }, [router, searchParams]);

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

// Wrap the component in Suspense for good practice
export default function AuthCallbackPage() {
  return (
    <Suspense>
      <AuthCallback />
    </Suspense>
  );
}