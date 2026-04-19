import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { QueryWrapper } from '../../test-utils/react-query-wrapper';
import { SubscriptionClient } from '../subscription-client';
import * as subscriptionHooks from '../../hooks/useSubscriptionSync';
import * as api from '../../services/api';
import type { User } from '../../services/api';

// Next navigation is used by router.push; a noop mock keeps the component renderable
// without a Next.js runtime.
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

// GlobalHeader pulls in next/link and auth context — stub it out so the test stays
// focused on the subscription actions. The mock target must match the exact specifier
// used in the component under test.
vi.mock('@/components/GlobalHeader', () => ({
  GlobalHeader: () => <div data-testid="global-header" />,
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockUser: User = {
  id: 1,
  spotifyId: 'spotify-1',
  displayName: 'Test User',
  email: 'user@example.com',
};

describe('SubscriptionClient — Manage Billing button', () => {
  let createPortalSessionSpy: Mock;

  beforeEach(() => {
    vi.clearAllMocks();
    createPortalSessionSpy = vi.fn().mockResolvedValue({
      portalUrl: 'https://billing.stripe.com/session/test',
    });
    vi.spyOn(api, 'createPortalSession').mockImplementation(createPortalSessionSpy);

    // window.location.assign is read-only on jsdom's default Location, so stub the whole
    // object. The original is restored by vi.unstubAllGlobals between tests.
    vi.stubGlobal('location', {
      ...window.location,
      assign: vi.fn(),
    });
  });

  it('renders the Manage Billing button when the user has an active subscription', () => {
    vi.spyOn(subscriptionHooks, 'useSubscriptionStatus').mockReturnValue({
      data: {
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
      },
      isLoading: false,
    } as ReturnType<typeof subscriptionHooks.useSubscriptionStatus>);

    render(
      <QueryWrapper>
        <SubscriptionClient initialUser={mockUser} />
      </QueryWrapper>
    );

    expect(screen.getByRole('button', { name: /manage billing/i })).toBeInTheDocument();
  });

  it('does not render the Manage Billing button when there is no active subscription', () => {
    vi.spyOn(subscriptionHooks, 'useSubscriptionStatus').mockReturnValue({
      data: { hasActiveSubscription: false },
      isLoading: false,
    } as ReturnType<typeof subscriptionHooks.useSubscriptionStatus>);

    render(
      <QueryWrapper>
        <SubscriptionClient initialUser={mockUser} />
      </QueryWrapper>
    );

    expect(screen.queryByRole('button', { name: /manage billing/i })).not.toBeInTheDocument();
  });

  it('calls createPortalSession and redirects to the returned portalUrl on click', async () => {
    vi.spyOn(subscriptionHooks, 'useSubscriptionStatus').mockReturnValue({
      data: {
        hasActiveSubscription: true,
        planName: 'Sync Plan',
        status: 'active',
      },
      isLoading: false,
    } as ReturnType<typeof subscriptionHooks.useSubscriptionStatus>);

    const user = userEvent.setup();
    render(
      <QueryWrapper>
        <SubscriptionClient initialUser={mockUser} />
      </QueryWrapper>
    );

    await user.click(screen.getByRole('button', { name: /manage billing/i }));

    await waitFor(() => {
      expect(createPortalSessionSpy).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(window.location.assign).toHaveBeenCalledWith(
        'https://billing.stripe.com/session/test'
      );
    });
  });
});
