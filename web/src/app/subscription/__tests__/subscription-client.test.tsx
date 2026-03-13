import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SubscriptionClient } from '../subscription-client';
import type { User } from '../../services/api';

// Mock the Supabase client used by fetchWithSupabaseAuth
vi.mock('@/lib/supabase/client', () => ({
  createClient: () => ({
    auth: {
      getSession: () =>
        Promise.resolve({
          data: { session: { access_token: 'test-token' } },
        }),
    },
  }),
}));

// Mock sonner toast
vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

const mockUser: User = {
  id: 1,
  spotifyId: 'spotify-123',
  displayName: 'Test User',
  email: 'test@example.com',
};

function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
}

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>
  );
}

function mockSubscriptionStatusResponse(status: Record<string, unknown>) {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
    ok: true,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: () => Promise.resolve(status),
  });
}

function mockCancelResponse(success = true) {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
    ok: true,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: () => Promise.resolve({ success }),
  });
}

describe('SubscriptionClient', () => {
  beforeEach(() => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReset();
  });

  describe('cancel subscription flow', () => {
    it('shows AlertDialog when cancel button is clicked instead of window.confirm', async () => {
      const user = userEvent.setup();
      const confirmSpy = vi.spyOn(window, 'confirm');

      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription')).toBeInTheDocument();
      });

      await user.click(screen.getByText('Cancel Subscription'));

      // AlertDialog should appear with its title
      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription?')).toBeInTheDocument();
      });

      // window.confirm should NOT have been called
      expect(confirmSpy).not.toHaveBeenCalled();
      confirmSpy.mockRestore();
    });

    it('AlertDialog mentions access until end of billing period', async () => {
      const user = userEvent.setup();

      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription')).toBeInTheDocument();
      });

      await user.click(screen.getByText('Cancel Subscription'));

      await waitFor(() => {
        expect(
          screen.getByText(/access until.*end of.*billing period/i)
        ).toBeInTheDocument();
      });
    });

    it('calls cancelSubscription API when confirmed', async () => {
      const user = userEvent.setup();

      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription')).toBeInTheDocument();
      });

      await user.click(screen.getByText('Cancel Subscription'));

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription?')).toBeInTheDocument();
      });

      // Mock the cancel API call
      mockCancelResponse(true);

      // Also mock the subscription status refetch that happens after invalidation
      mockSubscriptionStatusResponse({
        hasActiveSubscription: false,
      });

      // Click the confirm button in the dialog
      const confirmButton = screen.getByRole('button', {
        name: /yes, cancel subscription/i,
      });
      await user.click(confirmButton);

      await waitFor(() => {
        // Verify the cancel API was called
        const fetchCalls = (global.fetch as ReturnType<typeof vi.fn>).mock
          .calls;
        const cancelCall = fetchCalls.find(
          (call: string[]) =>
            typeof call[0] === 'string' &&
            call[0].includes('/subscription/cancel')
        );
        expect(cancelCall).toBeDefined();
        expect(cancelCall![1]).toMatchObject({ method: 'POST' });
      });
    });

    it('closes dialog without action when dismissed', async () => {
      const user = userEvent.setup();

      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription')).toBeInTheDocument();
      });

      await user.click(screen.getByText('Cancel Subscription'));

      await waitFor(() => {
        expect(screen.getByText('Cancel Subscription?')).toBeInTheDocument();
      });

      // Click the "Keep Subscription" button
      const keepButton = screen.getByRole('button', {
        name: /keep subscription/i,
      });
      await user.click(keepButton);

      // Dialog should close
      await waitFor(() => {
        expect(
          screen.queryByText('Cancel Subscription?')
        ).not.toBeInTheDocument();
      });

      // Only the initial subscription status fetch should have been called
      const fetchCalls = (global.fetch as ReturnType<typeof vi.fn>).mock.calls;
      const cancelCall = fetchCalls.find(
        (call: string[]) =>
          typeof call[0] === 'string' &&
          call[0].includes('/subscription/cancel')
      );
      expect(cancelCall).toBeUndefined();
    });

    it('displays plan data from API, not hardcoded values', async () => {
      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Premium Sync',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        // The plan name from the API should be displayed
        expect(screen.getByText('Premium Sync')).toBeInTheDocument();
      });
    });

    it('displays $5/month pricing', async () => {
      mockSubscriptionStatusResponse({
        hasActiveSubscription: false,
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('$5')).toBeInTheDocument();
        expect(screen.getByText('/month')).toBeInTheDocument();
      });
    });
  });

  describe('resume subscription flow', () => {
    it('shows Resume Subscription button when status is cancel_at_period_end', async () => {
      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'cancel_at_period_end',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Resume Subscription')).toBeInTheDocument();
      });

      // Should also show the cancellation pending message
      expect(screen.getByText(/cancellation pending/i)).toBeInTheDocument();
    });

    it('does not show Cancel Subscription button when status is cancel_at_period_end', async () => {
      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'cancel_at_period_end',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Resume Subscription')).toBeInTheDocument();
      });

      // Cancel Subscription button should NOT be present
      expect(
        screen.queryByRole('button', { name: /cancel subscription/i })
      ).not.toBeInTheDocument();
    });

    it('calls resume API when Resume Subscription button is clicked', async () => {
      const user = userEvent.setup();

      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'cancel_at_period_end',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      renderWithProviders(<SubscriptionClient initialUser={mockUser} />);

      await waitFor(() => {
        expect(screen.getByText('Resume Subscription')).toBeInTheDocument();
      });

      // Mock the resume API call
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
        ok: true,
        headers: new Headers({ 'content-type': 'application/json' }),
        json: () =>
          Promise.resolve({ message: 'Subscription resumed successfully' }),
      });

      // Mock the subscription status refetch
      mockSubscriptionStatusResponse({
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
        currentPeriodEnd: '2026-04-09T00:00:00Z',
      });

      await user.click(screen.getByText('Resume Subscription'));

      await waitFor(() => {
        const fetchCalls = (global.fetch as ReturnType<typeof vi.fn>).mock
          .calls;
        const resumeCall = fetchCalls.find(
          (call: string[]) =>
            typeof call[0] === 'string' &&
            call[0].includes('/subscription/resume')
        );
        expect(resumeCall).toBeDefined();
        expect(resumeCall![1]).toMatchObject({ method: 'POST' });
      });
    });
  });
});
