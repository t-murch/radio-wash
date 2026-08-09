import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { NextResponse } from 'next/server';
import { GET } from '../route';
import { createClient } from '@/lib/supabase/server';

vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('next/server', () => ({
  NextResponse: {
    redirect: vi.fn(),
  },
}));

global.fetch = vi.fn();

const request = (url: string, headers: Record<string, string> = {}) =>
  ({
    url,
    headers: { get: (k: string) => headers[k.toLowerCase()] ?? null },
  } as unknown as Request);

describe('Auth Callback Route', () => {
  let mockSupabase: {
    auth: {
      exchangeCodeForSession: Mock;
    };
  };

  beforeEach(() => {
    mockSupabase = {
      auth: {
        exchangeCodeForSession: vi.fn().mockResolvedValue({ error: null }),
      },
    };

    (createClient as Mock).mockResolvedValue(mockSupabase);
    process.env.NODE_ENV = 'development';
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.clearAllMocks();
  });

  it('exchanges the code and sends a new user into onboarding', async () => {
    await GET(request('https://radiowash.com/api/auth/callback?code=abc123'));

    expect(mockSupabase.auth.exchangeCodeForSession).toHaveBeenCalledWith(
      'abc123'
    );
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('honours a relative next parameter', async () => {
    await GET(
      request(
        'https://radiowash.com/api/auth/callback?code=abc&next=/dashboard'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/dashboard'
    );
  });

  it('refuses an absolute next parameter so the callback is not an open redirect', async () => {
    await GET(
      request(
        'https://radiowash.com/api/auth/callback?code=abc&next=https://evil.example.com'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('refuses a protocol-relative next parameter', async () => {
    await GET(
      request(
        'https://radiowash.com/api/auth/callback?code=abc&next=//evil.example.com'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('sends the user back to /auth with a message when the code is missing', async () => {
    await GET(request('https://radiowash.com/api/auth/callback'));

    expect(mockSupabase.auth.exchangeCodeForSession).not.toHaveBeenCalled();
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      expect.stringContaining('/auth?error=')
    );
  });

  it('sends the user back to /auth when the exchange fails', async () => {
    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({
      error: new Error('invalid grant'),
    });
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await GET(request('https://radiowash.com/api/auth/callback?code=bad'));

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      expect.stringContaining('/auth?error=')
    );
  });

  it('uses the forwarded host so redirects survive a load balancer', async () => {
    process.env.NODE_ENV = 'production';

    await GET(
      request('http://internal:3000/api/auth/callback?code=abc', {
        'x-forwarded-host': 'radiowash.com',
      })
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('redirects on the host the browser addressed, not the one in request.url', async () => {
    // Next's dev server rewrites request.url's host to localhost; the session
    // cookie was just set for the host the browser is actually on.
    await GET(
      request('https://localhost:3000/api/auth/callback?code=abc', {
        host: '127.0.0.1:3000',
      })
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/onboarding'
    );
  });

  it('does not forward provider tokens to the API', async () => {
    // Neither identity provider grants music access, so there is nothing to sync
    // here. Apple Music is authorized separately through MusicKit.
    await GET(request('https://radiowash.com/api/auth/callback?code=abc123'));

    expect(global.fetch).not.toHaveBeenCalled();
  });
});
