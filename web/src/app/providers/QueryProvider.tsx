'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { authManager } from '@/services/auth';
import { ApiError } from '@/services/api';

const isAuthError = (error: unknown) =>
  (error instanceof ApiError && error.status === 401) ||
  (error instanceof Error && error.message.includes('not authenticated'));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // Don't retry auth errors
        if (isAuthError(error)) {
          return false;
        }
        return failureCount < 3;
      },
    },
    mutations: {
      retry: (failureCount, error) => {
        // Don't retry auth errors
        if (isAuthError(error)) {
          return false;
        }
        return failureCount < 2;
      },
    },
  },
});

// Global error handler for auth issues
queryClient.setMutationDefaults(['auth'], {
  onError: (error: unknown) => {
    if (isAuthError(error)) {
      authManager.logout();
      window.location.href = '/auth';
    }
  },
});

export function QueryProvider({ children }: { children: React.ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      {children}
      {process.env.NODE_ENV === 'development' && (
        <ReactQueryDevtools initialIsOpen={false} />
      )}
    </QueryClientProvider>
  );
}
