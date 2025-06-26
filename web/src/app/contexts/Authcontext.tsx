'use client';

import {
  createContext,
  useState,
  useEffect,
  useCallback,
  useMemo,
} from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getMe,
  User,
  logout as apiLogout,
  API_BASE_URL,
} from '../services/api';

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: () => void;
  logout: () => void; // Changed to void for simplicity with hard redirect
}

export const AuthContext = createContext<AuthContextType | undefined>(
  undefined
);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();

  // This is a great pattern from your original file to prevent a 401 flash on load.
  const [isInitialCheckComplete, setInitialCheckComplete] = useState(false);

  const {
    data: user,
    isLoading: isUserLoading,
    isError,
  } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    retry: false,
    refetchOnWindowFocus: false,
    // This `enabled` flag is crucial.
    enabled: isInitialCheckComplete,
  });

  // Once the component mounts, we allow the 'me' query to run.
  useEffect(() => {
    setInitialCheckComplete(true);
  }, []);

  const login = () => {
    // Redirects to the backend to start the Spotify OAuth flow.
    window.location.href = `${API_BASE_URL}/auth/spotify/login`;
  };

  const logout = useCallback(() => {
    // Call the API to clear the server-side cookie.
    apiLogout().finally(() => {
      // Manually clear the user data from the cache.
      queryClient.setQueryData(['me'], null);
      // Forcibly redirect to the auth page to ensure a clean state.
      window.location.href = '/auth';
    });
  }, [queryClient]);

  const authContextValue = useMemo(
    () => ({
      user: user ?? null,
      // The user is authenticated only if the query succeeds.
      isAuthenticated: !!user && !isError,
      // Loading is true until the initial check is complete and the user query is settled.
      isLoading: !isInitialCheckComplete || isUserLoading,
      login,
      logout,
    }),
    [user, isUserLoading, isInitialCheckComplete, isError, logout]
  );

  return (
    <AuthContext.Provider value={authContextValue}>
      {children}
    </AuthContext.Provider>
  );
}
