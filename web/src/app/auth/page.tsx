'use client';

import { getLoginUrl, handleCallback } from '@/services/api';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState, Suspense } from 'react';

function AuthPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const code = searchParams.get('code');
    const state = searchParams.get('state');

    // If no code, user needs to authenticate
    if (!code) {
      handleLogin();
      return;
    }

    const handleCallbackAsync = async (code: string, state: string) => {
      try {
        setLoading(true);
        setError(null);

        const data = await handleCallback(code, state);

        // Store token and user data in localStorage
        localStorage.setItem('radiowash_token', data.token);
        localStorage.setItem('radiowash_user', JSON.stringify(data.user));

        // Redirect to dashboard
        router.push('/');
      } catch (error) {
        console.error('Authentication error:', error);
        setError('Authentication failed. Please try again.');
      } finally {
        setLoading(false);
      }
    };

    // Handle callback with code
    handleCallbackAsync(code, state || '');
  }, [router, searchParams]);

  const handleLogin = async () => {
    try {
      setLoading(true);
      setError(null);

      const data = await getLoginUrl();

      // Redirect to Spotify authorization page
      window.location.href = data;
    } catch (error) {
      console.error('Authentication error:', error);
      setError('Failed to start authentication process. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-50 p-4">
      <div className="w-full max-w-md p-8 space-y-8 bg-white rounded-xl shadow-md">
        <div className="text-center">
          <h1 className="text-3xl font-extrabold text-gray-900">RadioWash</h1>
          <p className="mt-2 text-gray-600">
            Create clean versions of your Spotify playlists
          </p>
        </div>

        {error && (
          <div
            className="p-4 mb-4 text-sm text-red-700 bg-red-100 rounded-lg"
            role="alert"
          >
            {error}
          </div>
        )}

        <div className="mt-8 space-y-6">
          <button
            onClick={handleLogin}
            disabled={loading}
            className="w-full flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50"
          >
            {loading ? 'Connecting...' : 'Connect with Spotify'}
          </button>

          {loading && (
            <div className="text-center text-gray-500 mt-4">
              <p>Please wait while we connect to Spotify...</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function AuthPageFallback() {
  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-50 p-4">
      <div className="w-full max-w-md p-8 space-y-8 bg-white rounded-xl shadow-md">
        <div className="text-center">
          <h1 className="text-3xl font-extrabold text-gray-900">RadioWash</h1>
          <p className="mt-2 text-gray-600">Loading authentication...</p>
        </div>
        <div className="flex justify-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-green-600"></div>
        </div>
      </div>
    </div>
  );
}

export default function AuthPage() {
  return (
    <Suspense fallback={<AuthPageFallback />}>
      <AuthPageContent />
    </Suspense>
  );
}
