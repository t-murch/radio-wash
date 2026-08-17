import { render, screen, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { SubscriptionSuccessClient } from '../subscription-success-client';
import {
  ApiError,
  completeCheckout,
  getSubscriptionStatus,
} from '@/services/api';
import { QueryWrapper } from '@/test-utils/react-query-wrapper';

// The real app router identity is stable across renders — mirror that, or the
// effect (which depends on `router`) re-runs on every render.
const { mockRouter, mockReplace } = vi.hoisted(() => {
  const replace = vi.fn();
  return {
    mockReplace: replace,
    mockRouter: { refresh: vi.fn(), push: vi.fn(), replace },
  };
});

vi.mock('next/navigation', () => ({
  useRouter: () => mockRouter,
}));

vi.mock('@/services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/api')>();
  return {
    ...actual,
    completeCheckout: vi.fn(),
    getSubscriptionStatus: vi.fn(),
  };
});

vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', () => ({
  GlobalHeader: () => <div data-testid="global-header" />,
}));

const user = {
  id: 1,
  supabaseId: 'supabase-1',
  displayName: 'Test User',
  email: 'test@example.com',
};

const activeStatus = {
  hasActiveSubscription: true,
  subscriptionId: 1,
  planName: 'Sync Plan',
  status: 'active',
  currentPeriodEnd: '2026-09-01T00:00:00Z',
  cancelAtPeriodEnd: false,
};

const inactiveStatus = {
  ...activeStatus,
  hasActiveSubscription: false,
  subscriptionId: null,
  planName: null,
  status: null,
  currentPeriodEnd: null,
};

const renderClient = (sessionId: string | null) =>
  render(
    <SubscriptionSuccessClient initialUser={user} sessionId={sessionId} />,
    { wrapper: QueryWrapper }
  );

const expectNoPaymentClaims = () => {
  expect(screen.queryByText(/payment received/i)).toBeNull();
  expect(screen.queryByText(/confirming your payment/i)).toBeNull();
  expect(screen.queryByText(/Subscription Successful!/i)).toBeNull();
};

describe('SubscriptionSuccessClient with a checkout session', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the activating state first, not an unconditional success', () => {
    // Reconciliation still in flight.
    (completeCheckout as Mock).mockReturnValue(new Promise(() => undefined));

    renderClient('cs_test_123');

    expect(
      screen.getByText(/Activating your subscription/i)
    ).toBeInTheDocument();
    expect(screen.queryByText(/Subscription Successful!/i)).toBeNull();
  });

  it('transitions to active when completeCheckout confirms the subscription', async () => {
    (completeCheckout as Mock).mockResolvedValue(activeStatus);

    renderClient('cs_test_123');

    expect(await screen.findByText(/Subscription Successful!/i)).toBeVisible();
    expect(completeCheckout).toHaveBeenCalledWith('cs_test_123');
    expect(getSubscriptionStatus).not.toHaveBeenCalled();
  });

  it('falls back to polling when completeCheckout fails transiently, then activates', async () => {
    (completeCheckout as Mock).mockRejectedValue(
      new ApiError(500, 'Internal Server Error')
    );
    (getSubscriptionStatus as Mock).mockResolvedValue(activeStatus);
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
    consoleErrorSpy.mockRestore();
  });

  it('retries completeCheckout during the poll window after a 500, then activates', async () => {
    (completeCheckout as Mock)
      .mockRejectedValueOnce(new ApiError(500, 'Internal Server Error'))
      .mockResolvedValue(activeStatus);
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    vi.useFakeTimers();
    renderClient('cs_test_123');

    // Tick 1 polls GET /status (inactive); tick 2 retries the idempotent
    // checkout/complete, which now succeeds.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4000);
    });

    expect(completeCheckout).toHaveBeenCalledTimes(2);
    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
    consoleErrorSpy.mockRestore();
  });

  it('shows a neutral unverified state on 404 without polling or payment claims', async () => {
    (completeCheckout as Mock).mockRejectedValue(
      new ApiError(404, 'Checkout session not found')
    );

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(
      screen.getByText(/couldn.t verify this checkout session/i)
    ).toBeInTheDocument();
    expectNoPaymentClaims();
    expect(
      screen.getByRole('button', { name: /Go to Subscription/i })
    ).toBeInTheDocument();

    // No poll loop starts on behalf of a rejected session.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(getSubscriptionStatus).not.toHaveBeenCalled();
    expect(completeCheckout).toHaveBeenCalledTimes(1);
  });

  it('shows a neutral unverified state on 403 without polling or payment claims', async () => {
    (completeCheckout as Mock).mockRejectedValue(
      new ApiError(403, 'Session belongs to another user')
    );

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });

    expect(
      screen.getByText(/couldn.t verify this checkout session/i)
    ).toBeInTheDocument();
    expectNoPaymentClaims();
    expect(getSubscriptionStatus).not.toHaveBeenCalled();
  });

  it('keeps polling in the delayed state and activates when the status flips', async () => {
    (completeCheckout as Mock).mockResolvedValue(inactiveStatus);
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(31000);
    });

    // Copy matches behavior: the page says it keeps checking, and it does.
    expect(screen.getByText(/Still activating/i)).toBeInTheDocument();
    expect(
      screen.getByText(/keeps checking automatically/i)
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Go to Dashboard/i })
    ).toBeInTheDocument();

    const pollsBeforeDelay = (getSubscriptionStatus as Mock).mock.calls.length;
    (getSubscriptionStatus as Mock).mockResolvedValue(activeStatus);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10000);
    });

    expect((getSubscriptionStatus as Mock).mock.calls.length).toBeGreaterThan(
      pollsBeforeDelay
    );
    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
  });

  it('activates from polling as soon as the status flips', async () => {
    (completeCheckout as Mock).mockResolvedValue(inactiveStatus);
    (getSubscriptionStatus as Mock)
      .mockResolvedValueOnce(inactiveStatus)
      .mockResolvedValue(activeStatus);

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(4000);
    });

    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
  });

  it('redirects to /auth when the initial reconcile hits a 401', async () => {
    (completeCheckout as Mock).mockRejectedValue(
      new ApiError(401, 'User not authenticated')
    );

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(mockReplace).toHaveBeenCalledWith('/auth');

    // No poll loop starts on behalf of an expired session.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect(getSubscriptionStatus).not.toHaveBeenCalled();
  });

  it('stops polling and redirects to /auth when the session expires mid-poll', async () => {
    (completeCheckout as Mock).mockResolvedValue(inactiveStatus);
    (getSubscriptionStatus as Mock).mockRejectedValue(
      new ApiError(401, 'User not authenticated')
    );

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/auth');

    const pollsAtRedirect = (getSubscriptionStatus as Mock).mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30000);
    });
    expect((getSubscriptionStatus as Mock).mock.calls.length).toBe(
      pollsAtRedirect
    );
  });

  it('stops checking at the hard cap and says so', async () => {
    (completeCheckout as Mock).mockResolvedValue(inactiveStatus);
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);

    vi.useFakeTimers();
    renderClient('cs_test_123');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10 * 60 * 1000 + 1000);
    });

    // Copy matches behavior: the page says it stopped checking, and it has.
    expect(screen.getByText(/Activation still pending/i)).toBeInTheDocument();
    expect(
      screen.getByText(/stopped checking automatically/i)
    ).toBeInTheDocument();
    expectNoPaymentClaims();

    const pollsAtCap = (getSubscriptionStatus as Mock).mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(60000);
    });
    expect((getSubscriptionStatus as Mock).mock.calls.length).toBe(pollsAtCap);
  });
});

describe('SubscriptionSuccessClient without a checkout session', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('checks the status neutrally and redirects inactive users to /subscription', async () => {
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);

    renderClient(null);

    // Neutral copy while checking — no payment language on a direct visit.
    expect(
      screen.getByText(/Checking subscription status/i)
    ).toBeInTheDocument();
    expectNoPaymentClaims();
    expect(screen.queryByText(/Activating your subscription/i)).toBeNull();

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/subscription');
    });
    expectNoPaymentClaims();
    expect(completeCheckout).not.toHaveBeenCalled();
  });

  it('shows the success view when the subscription is already active', async () => {
    (getSubscriptionStatus as Mock).mockResolvedValue(activeStatus);

    renderClient(null);

    expect(await screen.findByText(/Subscription Successful!/i)).toBeVisible();
    expect(mockReplace).not.toHaveBeenCalled();
    expect(completeCheckout).not.toHaveBeenCalled();
  });

  it('retries a transiently failing status check before giving up', async () => {
    (getSubscriptionStatus as Mock)
      .mockRejectedValueOnce(new ApiError(500, 'Internal Server Error'))
      .mockResolvedValue(activeStatus);
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    vi.useFakeTimers();
    renderClient(null);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    // A subscribed user is not bounced by one transient failure.
    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
    expect(mockReplace).not.toHaveBeenCalled();
    expect(getSubscriptionStatus).toHaveBeenCalledTimes(2);
    consoleErrorSpy.mockRestore();
    vi.useRealTimers();
  });

  it('redirects to /subscription when the status check keeps failing', async () => {
    (getSubscriptionStatus as Mock).mockRejectedValue(
      new ApiError(500, 'Internal Server Error')
    );
    const consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);

    vi.useFakeTimers();
    renderClient(null);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });

    expect(mockReplace).toHaveBeenCalledWith('/subscription');
    expect(getSubscriptionStatus).toHaveBeenCalledTimes(2);
    expectNoPaymentClaims();
    consoleErrorSpy.mockRestore();
    vi.useRealTimers();
  });

  it('redirects to /auth when the status check hits a 401', async () => {
    (getSubscriptionStatus as Mock).mockRejectedValue(
      new ApiError(401, 'User not authenticated')
    );

    renderClient(null);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/auth');
    });
    // 401 is terminal — no retry.
    expect(getSubscriptionStatus).toHaveBeenCalledTimes(1);
    expectNoPaymentClaims();
  });
});

describe('SubscriptionSuccessClient without fake timers', () => {
  it('keeps activating while the status is not yet active', async () => {
    (completeCheckout as Mock).mockResolvedValue(inactiveStatus);
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);

    renderClient('cs_test_123');

    await waitFor(() => {
      expect(completeCheckout).toHaveBeenCalled();
    });
    expect(
      screen.getByText(/Activating your subscription/i)
    ).toBeInTheDocument();
    expect(screen.queryByText(/Subscription Successful!/i)).toBeNull();
  });
});
