import { describe, it, expect, vi, beforeEach } from 'vitest';

// Mock Supabase client before importing api module
vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(() => ({
    auth: {
      getSession: vi.fn().mockResolvedValue({
        data: { session: { access_token: 'test-token' } },
      }),
    },
  })),
}));

// Mock global fetch
const mockFetch = vi.fn();
vi.stubGlobal('fetch', mockFetch);

function mockFetchResponse(body: unknown, ok = true, status = 200) {
  return {
    ok,
    status,
    statusText: ok ? 'OK' : 'Bad Request',
    headers: new Headers({ 'content-type': 'application/json' }),
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  };
}

describe('subscribeToSync', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('sends planId (not planPriceId) in checkout request', async () => {
    const plans = [
      {
        id: 1,
        name: 'Monthly',
        price: 499,
        billingPeriod: 'monthly',
        stripePriceId: 'price_abc123',
        features: ['sync'],
        isActive: true,
      },
    ];

    // First call: GET /subscription/plans
    mockFetch.mockResolvedValueOnce(mockFetchResponse(plans));
    // Second call: POST /subscription/checkout
    mockFetch.mockResolvedValueOnce(
      mockFetchResponse({ checkoutUrl: 'https://checkout.stripe.com/session_123' })
    );

    const { subscribeToSync } = await import('../api');
    const result = await subscribeToSync();

    expect(result).toEqual({ checkoutUrl: 'https://checkout.stripe.com/session_123' });

    // Verify the checkout POST body contains planId, not planPriceId
    const checkoutCall = mockFetch.mock.calls[1];
    const checkoutBody = JSON.parse(checkoutCall[1].body);
    expect(checkoutBody).toEqual({ planId: 1 });
    expect(checkoutBody).not.toHaveProperty('planPriceId');
  });

  it('finds the monthly plan and validates it is active', async () => {
    const plans = [
      {
        id: 1,
        name: 'Yearly',
        price: 4999,
        billingPeriod: 'yearly',
        stripePriceId: 'price_yearly',
        features: ['sync'],
        isActive: true,
      },
      {
        id: 2,
        name: 'Monthly',
        price: 499,
        billingPeriod: 'monthly',
        stripePriceId: 'price_monthly',
        features: ['sync'],
        isActive: true,
      },
    ];

    mockFetch.mockResolvedValueOnce(mockFetchResponse(plans));
    mockFetch.mockResolvedValueOnce(
      mockFetchResponse({ checkoutUrl: 'https://checkout.stripe.com/session_456' })
    );

    const { subscribeToSync } = await import('../api');
    const result = await subscribeToSync();

    expect(result).toEqual({ checkoutUrl: 'https://checkout.stripe.com/session_456' });

    // Should use the monthly plan (id: 2), not the first plan (id: 1)
    const checkoutCall = mockFetch.mock.calls[1];
    const checkoutBody = JSON.parse(checkoutCall[1].body);
    expect(checkoutBody).toEqual({ planId: 2 });
  });

  it('throws if no plans are available', async () => {
    mockFetch.mockResolvedValueOnce(mockFetchResponse([]));

    const { subscribeToSync } = await import('../api');
    await expect(subscribeToSync()).rejects.toThrow('No subscription plans available');
  });

  it('throws if no active monthly plan is found', async () => {
    const plans = [
      {
        id: 1,
        name: 'Monthly',
        price: 499,
        billingPeriod: 'monthly',
        stripePriceId: 'price_monthly',
        features: ['sync'],
        isActive: false, // inactive
      },
    ];

    mockFetch.mockResolvedValueOnce(mockFetchResponse(plans));

    const { subscribeToSync } = await import('../api');
    await expect(subscribeToSync()).rejects.toThrow(
      'No active monthly subscription plan found'
    );
  });

  it('throws if only yearly plans exist', async () => {
    const plans = [
      {
        id: 1,
        name: 'Yearly',
        price: 4999,
        billingPeriod: 'yearly',
        stripePriceId: 'price_yearly',
        features: ['sync'],
        isActive: true,
      },
    ];

    mockFetch.mockResolvedValueOnce(mockFetchResponse(plans));

    const { subscribeToSync } = await import('../api');
    await expect(subscribeToSync()).rejects.toThrow(
      'No active monthly subscription plan found'
    );
  });
});
