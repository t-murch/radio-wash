import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import {
  ApiError,
  API_BASE_URL,
  fetchWithSupabaseAuth,
  subscribeToSync,
} from '../api';
import { createClient } from '@/lib/supabase/client';

vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(),
}));

const mockFetch = global.fetch as Mock;

const problemResponse = (status: number, body: unknown) => ({
  ok: false,
  status,
  statusText: 'Error',
  text: async () => JSON.stringify(body),
  headers: new Headers({ 'content-type': 'application/problem+json' }),
});

describe('api error handling', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    (createClient as Mock).mockReturnValue({
      auth: {
        getSession: vi
          .fn()
          .mockResolvedValue({ data: { session: { access_token: 'token' } } }),
      },
    });
    consoleErrorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
  });

  it('throws ApiError carrying status and Problem Details fields', async () => {
    mockFetch.mockResolvedValue(
      problemResponse(409, {
        title: 'Already subscribed',
        detail: 'You already have an active subscription.',
        status: 409,
        type: 'https://radiowash.app/problems/already-subscribed',
      })
    );

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(409);
    expect(error.message).toBe('Already subscribed');
    expect(error.detail).toBe('You already have an active subscription.');
    expect(error.problemType).toBe(
      'https://radiowash.app/problems/already-subscribed'
    );
    // The boundary still logs the raw failure once.
    expect(consoleErrorSpy).toHaveBeenCalled();
  });

  it('uses detail as message when the problem has no title', async () => {
    mockFetch.mockResolvedValue(
      problemResponse(503, { detail: 'Subscriptions are unavailable.' })
    );

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(503);
    expect(error.message).toBe('Subscriptions are unavailable.');
  });

  it('falls back to the raw body for non-JSON error responses', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      text: async () => 'Something broke',
      headers: new Headers({ 'content-type': 'text/plain' }),
    });

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(500);
    expect(error.message).toBe('Something broke');
    expect(error.detail).toBeUndefined();
  });

  it('ignores non-string title/detail and falls back to the raw body', async () => {
    mockFetch.mockResolvedValue(
      problemResponse(500, { title: 123, detail: { nested: true } })
    );

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(typeof error.message).toBe('string');
    expect(error.message).toBe(
      JSON.stringify({ title: 123, detail: { nested: true } })
    );
    expect(error.detail).toBeUndefined();
  });

  it('truncates long non-JSON error bodies to ~200 chars', async () => {
    const longBody = 'x'.repeat(1000);
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      text: async () => longBody,
      headers: new Headers({ 'content-type': 'text/html' }),
    });

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.message.length).toBeLessThanOrEqual(201);
    expect(error.message.startsWith('x'.repeat(200))).toBe(true);
  });

  it('falls back to statusText when the error body is empty', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 502,
      statusText: 'Bad Gateway',
      text: async () => '',
      headers: new Headers(),
    });

    const error = await fetchWithSupabaseAuth(`${API_BASE_URL}/test`).catch(
      (e) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(502);
    expect(error.message).toBe('Bad Gateway');
  });

  it('returns parsed JSON on success', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({ hello: 'world' }),
      headers: new Headers({ 'content-type': 'application/json' }),
    });

    await expect(fetchWithSupabaseAuth(`${API_BASE_URL}/test`)).resolves.toEqual(
      { hello: 'world' }
    );
  });
});

describe('subscribeToSync', () => {
  beforeEach(() => {
    (createClient as Mock).mockReturnValue({
      auth: {
        getSession: vi
          .fn()
          .mockResolvedValue({ data: { session: { access_token: 'token' } } }),
      },
    });
  });

  it('posts planId null and a client request id straight to checkout', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({ checkoutUrl: 'https://checkout.stripe.com/x' }),
      headers: new Headers({ 'content-type': 'application/json' }),
    });

    const result = await subscribeToSync();

    expect(result).toEqual({ checkoutUrl: 'https://checkout.stripe.com/x' });
    // No /plans pre-fetch — a single POST to /checkout.
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(`${API_BASE_URL}/subscription/checkout`);
    expect(options.method).toBe('POST');
    const body = JSON.parse(options.body);
    expect(body.planId).toBeNull();
    expect(body.clientRequestId).toEqual(expect.any(String));
    expect(body.clientRequestId.length).toBeGreaterThan(0);
  });
});
