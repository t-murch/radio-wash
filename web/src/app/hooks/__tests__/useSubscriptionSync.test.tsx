import { renderHook, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactNode } from 'react';
import {
  useSubscribeToSync,
  useSubscriptionStatus,
} from '../useSubscriptionSync';
import { getSubscriptionStatus, subscribeToSync } from '@/services/api';

vi.mock('@/services/api', () => ({
  getSubscriptionStatus: vi.fn(),
  getCurrentSubscription: vi.fn(),
  enableSyncForJob: vi.fn(),
  subscribeToSync: vi.fn(),
  getSyncConfigs: vi.fn(),
}));

// Wrapper whose defaults RETRY mutations, mirroring QueryProvider — the hook
// must override this with retry: false to avoid duplicate checkout sessions.
const createRetryingWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: 2, retryDelay: 1 },
    },
  });
  const RetryingWrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return RetryingWrapper;
};

describe('useSubscriptionStatus', () => {
  it('refetches on mount even when cached data is still fresh', async () => {
    (getSubscriptionStatus as Mock).mockResolvedValue({
      hasActiveSubscription: true,
    });
    // Mirror QueryProvider's 5-minute staleTime: without refetchOnMount
    // 'always', a mounting manage view would show the stale cached value
    // (e.g. after changes in the Stripe billing portal).
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, staleTime: 1000 * 60 * 5 },
      },
    });
    queryClient.setQueryData(['subscription-status'], {
      hasActiveSubscription: false,
    });
    const StaleWrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );

    const { result } = renderHook(() => useSubscriptionStatus(), {
      wrapper: StaleWrapper,
    });

    await waitFor(() => {
      expect(getSubscriptionStatus).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(result.current.data?.hasActiveSubscription).toBe(true);
    });
  });
});

describe('useSubscribeToSync', () => {
  let locationStub: { href: string };

  beforeEach(() => {
    locationStub = { href: 'http://localhost/' };
    vi.stubGlobal('location', locationStub);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('does not retry a failed checkout even when defaults retry mutations', async () => {
    (subscribeToSync as Mock).mockRejectedValue(new Error('boom'));

    const { result } = renderHook(() => useSubscribeToSync(), {
      wrapper: createRetryingWrapper(),
    });

    await expect(result.current.mutateAsync()).rejects.toThrow('boom');
    // A retried mutation would create additional Stripe checkout sessions.
    expect(subscribeToSync).toHaveBeenCalledTimes(1);
  });

  it('rejects instead of navigating when the checkout URL is missing', async () => {
    (subscribeToSync as Mock).mockResolvedValue({});

    const { result } = renderHook(() => useSubscribeToSync(), {
      wrapper: createRetryingWrapper(),
    });

    await expect(result.current.mutateAsync()).rejects.toThrow(
      'Checkout could not be started'
    );
    expect(locationStub.href).toBe('http://localhost/');
  });

  it('redirects to the checkout URL on success', async () => {
    (subscribeToSync as Mock).mockResolvedValue({
      checkoutUrl: 'https://checkout.stripe.com/session/abc',
    });

    const { result } = renderHook(() => useSubscribeToSync(), {
      wrapper: createRetryingWrapper(),
    });

    result.current.mutate();

    await waitFor(() => {
      expect(locationStub.href).toBe('https://checkout.stripe.com/session/abc');
    });
  });
});
