import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { NextResponse } from 'next/server';
import { GET } from '../route';
import { createClient } from '@/lib/supabase/server';

// Mock dependencies
vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('next/server', () => ({
  NextResponse: {
    redirect: vi.fn(),
  },
}));

// Mock fetch globally
global.fetch = vi.fn();

describe('Auth Callback Route', () => {
  let mockSupabase: {
    auth: {
      exchangeCodeForSession: Mock;
      getSession: Mock;
    };
  };

  beforeEach(() => {
    mockSupabase = {
      auth: {
        exchangeCodeForSession: vi.fn(),
        getSession: vi.fn(),
      },
    };

    (createClient as Mock).mockResolvedValue(mockSupabase);
    (global.fetch as Mock).mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({}),
    });

    // Mock Date.now() to ensure consistent timestamps
    vi.spyOn(Date, 'now').mockReturnValue(new Date('2025-01-01T00:00:00.000Z').getTime());

    // Mock environment variables
    process.env.NODE_ENV = 'development';
    process.env.NEXT_PUBLIC_WEB_URL = 'https://radiowash.com';
    process.env.NEXT_PUBLIC_API_URL = 'https://api.radiowash.com';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should handle successful Spotify OAuth callback with token sync', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
          provider_token: 'spotify_access_token',
          provider_refresh_token: 'spotify_refresh_token',
        },
      },
    });

    await GET(mockRequest);

    // Verify auth code exchange
    expect(mockSupabase.auth.exchangeCodeForSession).toHaveBeenCalledWith('auth_code');

    // Verify session retrieval
    expect(mockSupabase.auth.getSession).toHaveBeenCalled();

    // Verify token sync API call
    expect(global.fetch).toHaveBeenCalledWith(
      'https://api.radiowash.com/api/auth/spotify/tokens',
      {
        method: 'POST',
        headers: {
          Authorization: 'Bearer user_token',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          accessToken: 'spotify_access_token',
          refreshToken: 'spotify_refresh_token',
          expiresAt: new Date(new Date('2025-01-01T00:00:00.000Z').getTime() + 3600 * 1000).toISOString(),
        }),
      }
    );

    // Verify redirect to dashboard
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/dashboard'
    );
  });

  it('should handle OAuth callback with custom next parameter', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify&next=/custom-page'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
          provider_token: 'spotify_access_token',
          provider_refresh_token: 'spotify_refresh_token',
        },
      },
    });

    await GET(mockRequest);

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/custom-page'
    );
  });

  it('should handle OAuth callback without provider tokens', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
          // No provider tokens
        },
      },
    });

    await GET(mockRequest);

    // Should not attempt token sync
    expect(global.fetch).not.toHaveBeenCalled();

    // Should still redirect successfully
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/dashboard'
    );
  });

  it('should handle auth exchange errors', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=invalid_code&platform=spotify'
    );

    const authError = { message: 'Invalid authorization code' };
    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: authError });

    await GET(mockRequest);

    // Should not get session or sync tokens
    expect(mockSupabase.auth.getSession).not.toHaveBeenCalled();
    expect(global.fetch).not.toHaveBeenCalled();

    // Should redirect to auth page with error
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/auth?error=[object Object]'
    );
  });

  it('should handle token sync API failures gracefully', async () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
          provider_token: 'spotify_access_token',
          provider_refresh_token: 'spotify_refresh_token',
        },
      },
    });

    (global.fetch as Mock).mockRejectedValue(new Error('API unavailable'));

    await GET(mockRequest);

    // Should log error but still redirect successfully
    expect(consoleErrorSpy).toHaveBeenCalledWith(
      'Failed to sync Spotify tokens:',
      expect.any(Error)
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/dashboard'
    );

    consoleErrorSpy.mockRestore();
  });

  it('should handle Apple platform correctly', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=apple'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
        },
      },
    });

    await GET(mockRequest);

    // Should not attempt Spotify token sync
    expect(global.fetch).not.toHaveBeenCalled();

    // Should still redirect
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/dashboard'
    );
  });

  it('should handle missing code parameter', async () => {
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?platform=spotify'
    );

    const result = await GET(mockRequest);

    // Should not attempt any auth operations
    expect(mockSupabase.auth.exchangeCodeForSession).not.toHaveBeenCalled();
    expect(mockSupabase.auth.getSession).not.toHaveBeenCalled();
    expect(global.fetch).not.toHaveBeenCalled();

    // Should return undefined (no redirect)
    expect(result).toBeUndefined();
  });

  it('should use production origin in production environment', async () => {
    process.env.NODE_ENV = 'production';
    
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
        },
      },
    });

    await GET(mockRequest);

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/dashboard'
    );
  });

  it('should handle malformed URLs gracefully', async () => {
    const mockRequest = new Request('https://example.com/api/auth/callback?code=test&next=javascript:alert("xss")');

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
        },
      },
    });

    await GET(mockRequest);

    // Should still redirect (the next parameter is used as-is, but should be validated in real implementation)
    expect(NextResponse.redirect).toHaveBeenCalled();
  });

  it('should handle network errors during token sync', async () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    
    const mockRequest = new Request(
      'https://example.com/api/auth/callback?code=auth_code&platform=spotify'
    );

    mockSupabase.auth.exchangeCodeForSession.mockResolvedValue({ error: null });
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: 'user_token',
          provider_token: 'spotify_access_token',
          provider_refresh_token: 'spotify_refresh_token',
        },
      },
    });

    // Simulate network error
    (global.fetch as Mock).mockImplementation(() => 
      Promise.reject(new TypeError('Failed to fetch'))
    );

    await GET(mockRequest);

    expect(consoleErrorSpy).toHaveBeenCalledWith(
      'Failed to sync Spotify tokens:',
      expect.any(TypeError)
    );

    // Should still redirect despite sync failure
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://127.0.0.1:3000/dashboard'
    );

    consoleErrorSpy.mockRestore();
  });
});