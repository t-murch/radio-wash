import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { SubscriptionClient } from '../subscription-client';
import {
  ApiError,
  createPortalSession,
  type SubscriptionStatus,
} from '@/services/api';
import {
  useSubscriptionStatus,
  useSubscribeToSync,
} from '@/hooks/useSubscriptionSync';
import { toast } from 'sonner';
import { QueryWrapper } from '@/test-utils/react-query-wrapper';

vi.mock('@/services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/api')>();
  return {
    ...actual,
    cancelSubscription: vi.fn(),
    createPortalSession: vi.fn(),
  };
});

vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('@/hooks/useSubscriptionSync', () => ({
  useSubscriptionStatus: vi.fn(),
  useSubscribeToSync: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', () => ({
  GlobalHeader: () => <div data-testid="global-header" />,
}));

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

const user = {
  id: 1,
  supabaseId: 'supabase-1',
  displayName: 'Test User',
  email: 'test@example.com',
};

const activeStatus: SubscriptionStatus = {
  hasActiveSubscription: true,
  subscriptionId: 1,
  planName: 'Sync Plan',
  status: 'active',
  currentPeriodEnd: '2026-09-01T00:00:00Z',
  cancelAtPeriodEnd: false,
};

const noSubscriptionStatus: SubscriptionStatus = {
  hasActiveSubscription: false,
  subscriptionId: null,
  planName: null,
  status: null,
  currentPeriodEnd: null,
  cancelAtPeriodEnd: false,
};

const setStatus = (status: SubscriptionStatus) => {
  (useSubscriptionStatus as Mock).mockReturnValue({
    data: status,
    isLoading: false,
  });
};

const setSubscribeMutation = (overrides = {}) => {
  (useSubscribeToSync as Mock).mockReturnValue({
    mutateAsync: vi.fn(),
    isPending: false,
    ...overrides,
  });
};

const renderClient = () =>
  render(<SubscriptionClient initialUser={user} />, { wrapper: QueryWrapper });

describe('SubscriptionClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setSubscribeMutation();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the already-subscribed message when checkout returns 409', async () => {
    setStatus(noSubscriptionStatus);
    setSubscribeMutation({
      mutateAsync: vi
        .fn()
        .mockRejectedValue(
          new ApiError(
            409,
            'Already subscribed',
            'You already have an active subscription.',
            'https://radiowash.com/problems/already-subscribed'
          )
        ),
    });
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    renderClient();
    await userEvent.click(
      screen.getByRole('button', { name: /Subscribe to Sync/i })
    );

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'You already have an active subscription'
      );
    });
    consoleErrorSpy.mockRestore();
  });

  it('shows the unavailable message when checkout is disabled (503)', async () => {
    setStatus(noSubscriptionStatus);
    setSubscribeMutation({
      mutateAsync: vi
        .fn()
        .mockRejectedValue(
          new ApiError(
            503,
            'Checkout disabled',
            'Subscriptions are temporarily unavailable. Please try again later.',
            'https://radiowash.com/problems/checkout-disabled'
          )
        ),
    });
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    renderClient();
    await userEvent.click(
      screen.getByRole('button', { name: /Subscribe to Sync/i })
    );

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Subscriptions are temporarily unavailable — please try again later'
      );
    });
    consoleErrorSpy.mockRestore();
  });

  it('shows a friendly message when checkout is rate limited (429)', async () => {
    setStatus(noSubscriptionStatus);
    // The rate limiter responds without a Problem Details body, so the
    // ApiError carries only a raw message.
    setSubscribeMutation({
      mutateAsync: vi
        .fn()
        .mockRejectedValue(new ApiError(429, 'Too Many Requests')),
    });
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    renderClient();
    await userEvent.click(
      screen.getByRole('button', { name: /Subscribe to Sync/i })
    );

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Too many attempts — please wait a minute and try again'
      );
    });
    consoleErrorSpy.mockRestore();
  });

  it('renders the scheduled-cancellation banner and hides the cancel button', () => {
    setStatus({ ...activeStatus, cancelAtPeriodEnd: true });

    renderClient();

    expect(screen.getByText(/Cancellation scheduled/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Cancel Subscription/i })
    ).toBeNull();
    // Billing can still be resumed through the portal.
    expect(
      screen.getByRole('button', { name: /Manage billing/i })
    ).toBeInTheDocument();
    expect(screen.getByText(/Access until:/i)).toBeInTheDocument();
  });

  it('shows the cancel button and real plan data for an active subscription', () => {
    setStatus(activeStatus);

    renderClient();

    expect(
      screen.getByRole('button', { name: /Cancel Subscription/i })
    ).toBeInTheDocument();
    expect(screen.getByText('Sync Plan')).toBeInTheDocument();
    expect(screen.getByText('active')).toBeInTheDocument();
    expect(screen.getByText(/Next billing:/i)).toBeInTheDocument();
    expect(screen.queryByText(/Cancellation scheduled/i)).toBeNull();
  });

  it('navigates to the billing portal URL from the Manage billing button', async () => {
    setStatus(activeStatus);
    (createPortalSession as Mock).mockResolvedValue({
      portalUrl: 'https://billing.stripe.com/session/xyz',
    });
    const locationStub = { href: 'http://localhost/' };
    vi.stubGlobal('location', locationStub);

    renderClient();
    await userEvent.click(
      screen.getByRole('button', { name: /Manage billing/i })
    );

    await waitFor(() => {
      expect(locationStub.href).toBe('https://billing.stripe.com/session/xyz');
    });
  });

  it('toasts an error when the portal session cannot be created', async () => {
    setStatus(activeStatus);
    // The backend returns 404 when no subscription exists for the user.
    (createPortalSession as Mock).mockRejectedValue(
      new ApiError(404, 'No active subscription found')
    );
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    renderClient();
    await userEvent.click(
      screen.getByRole('button', { name: /Manage billing/i })
    );

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Could not open the billing portal. Please try again.'
      );
    });
    consoleErrorSpy.mockRestore();
  });
});
