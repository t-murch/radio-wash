import { render, screen, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { SubscriptionSuccessClient } from '../subscription-success-client';
import { completeCheckout, getSubscriptionStatus } from '@/services/api';
import { QueryWrapper } from '@/test-utils/react-query-wrapper';

vi.mock('@/services/api', () => ({
  completeCheckout: vi.fn(),
  getSubscriptionStatus: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', () => ({
  GlobalHeader: () => <div data-testid="global-header" />,
}));

const user = {
  id: 1,
  spotifyId: 'spotify-1',
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

describe('SubscriptionSuccessClient', () => {
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

  it('falls back to polling when completeCheckout fails, then activates', async () => {
    (completeCheckout as Mock).mockRejectedValue(new Error('404'));
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

  it('shows the delayed state after polling times out without activation', async () => {
    (getSubscriptionStatus as Mock).mockResolvedValue(inactiveStatus);

    vi.useFakeTimers();
    // No session id (old bookmark) — goes straight to polling.
    renderClient(null);

    expect(
      screen.getByText(/Activating your subscription/i)
    ).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(31000);
    });

    expect(screen.getByText(/Payment received/i)).toBeInTheDocument();
    expect(
      screen.getByText(/activation is taking longer than expected/i)
    ).toBeInTheDocument();
    // Reassurance, not an error: the dashboard link is offered.
    expect(
      screen.getByRole('button', { name: /Go to Dashboard/i })
    ).toBeInTheDocument();
    expect(completeCheckout).not.toHaveBeenCalled();
    expect(getSubscriptionStatus).toHaveBeenCalled();
  });

  it('activates from polling as soon as the status flips', async () => {
    (getSubscriptionStatus as Mock)
      .mockResolvedValueOnce(inactiveStatus)
      .mockResolvedValue(activeStatus);

    vi.useFakeTimers();
    renderClient(null);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(4000);
    });

    expect(screen.getByText(/Subscription Successful!/i)).toBeInTheDocument();
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
