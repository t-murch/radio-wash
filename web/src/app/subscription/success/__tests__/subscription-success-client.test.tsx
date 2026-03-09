import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SubscriptionSuccessClient } from '../subscription-success-client';

// Mock the useSubscriptionSync hook module
const mockUseVerifyCheckoutSession = vi.fn();
vi.mock('@/hooks/useSubscriptionSync', () => ({
  useVerifyCheckoutSession: (...args: unknown[]) =>
    mockUseVerifyCheckoutSession(...args),
}));

// Mock next/navigation (extends the global mock from setup.ts)
const mockGet = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    refresh: vi.fn(),
    push: vi.fn(),
    replace: vi.fn(),
  }),
  useSearchParams: () => ({
    get: mockGet,
  }),
}));

// Mock GlobalHeader to keep tests focused
vi.mock('@/components/GlobalHeader', () => ({
  GlobalHeader: () => <div data-testid="global-header">Header</div>,
}));

const mockUser = {
  id: 1,
  spotifyId: 'spotify-123',
  displayName: 'Test User',
  email: 'test@example.com',
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>
  );
}

describe('SubscriptionSuccessClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockReturnValue('cs_test_session_123');
  });

  it('renders loading state while verifying', () => {
    mockUseVerifyCheckoutSession.mockReturnValue({
      isVerified: false,
      isLoading: true,
      subscription: undefined,
      isTimeout: false,
    });

    renderWithProviders(<SubscriptionSuccessClient initialUser={mockUser} />);

    expect(screen.getByText(/verifying/i)).toBeInTheDocument();
  });

  it('shows success UI when session is verified', () => {
    mockUseVerifyCheckoutSession.mockReturnValue({
      isVerified: true,
      isLoading: false,
      subscription: {
        id: 1,
        status: 'active',
        plan: { name: 'Sync Monthly' },
        createdAt: new Date().toISOString(),
      },
      isTimeout: false,
    });

    renderWithProviders(<SubscriptionSuccessClient initialUser={mockUser} />);

    expect(screen.getByText(/subscription successful/i)).toBeInTheDocument();
    expect(screen.getByText(/go to dashboard/i)).toBeInTheDocument();
  });

  it('shows timeout/fallback UI when verification fails', () => {
    mockUseVerifyCheckoutSession.mockReturnValue({
      isVerified: false,
      isLoading: false,
      subscription: undefined,
      isTimeout: true,
    });

    renderWithProviders(<SubscriptionSuccessClient initialUser={mockUser} />);

    expect(
      screen.getByText(/could not confirm/i)
    ).toBeInTheDocument();
  });

  it('shows fallback UI when no session_id is present', () => {
    mockGet.mockReturnValue(null);
    mockUseVerifyCheckoutSession.mockReturnValue({
      isVerified: false,
      isLoading: false,
      subscription: undefined,
      isTimeout: false,
    });

    renderWithProviders(<SubscriptionSuccessClient initialUser={mockUser} />);

    expect(
      screen.getByText(/no checkout session found/i)
    ).toBeInTheDocument();
  });

  it('passes session_id to useVerifyCheckoutSession', () => {
    mockGet.mockReturnValue('cs_test_abc');
    mockUseVerifyCheckoutSession.mockReturnValue({
      isVerified: false,
      isLoading: true,
      subscription: undefined,
      isTimeout: false,
    });

    renderWithProviders(<SubscriptionSuccessClient initialUser={mockUser} />);

    expect(mockUseVerifyCheckoutSession).toHaveBeenCalledWith('cs_test_abc');
  });
});
