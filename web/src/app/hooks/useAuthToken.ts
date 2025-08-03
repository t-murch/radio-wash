import { useState, useEffect } from 'react';
import { createClient } from '@/lib/supabase/client';

/**
 * Hook to get the current user's authentication token
 * Returns null if user is not authenticated
 */
export function useAuthToken() {
  const [authToken, setAuthToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const supabase = createClient();

    const getToken = async () => {
      try {
        const {
          data: { session },
        } = await supabase.auth.getSession();
        setAuthToken(session?.access_token || null);
      } catch (error) {
        setAuthToken(null);

        console.error('Error fetching auth token:', error);
      } finally {
        setIsLoading(false);
      }
    };

    // Get initial token
    getToken();

    // Listen for auth state changes
    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, session) => {
      setAuthToken(session?.access_token || null);
      setIsLoading(false);
    });

    return () => subscription.unsubscribe();
  }, []);

  return { authToken, isLoading };
}