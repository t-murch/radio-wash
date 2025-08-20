import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { createClient } from '../../lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';
import { signInWithSpotify } from '../actions';

// Mock dependencies
vi.mock('@/lib/supabase/server');
vi.mock('next/headers');
vi.mock('next/navigation');

describe('Auth Actions', () => {
  let mockSupabase: {
    auth: {
      signInWithOAuth: Mock;
    };
  };
  let mockHeaders: Mock;

  beforeEach(() => {
    mockSupabase = {
      auth: {
        signInWithOAuth: vi.fn(),
      },
    };

    mockHeaders = vi.fn().mockReturnValue({
      get: vi.fn().mockReturnValue('http://localhost:3000'),
    });

    (createClient as Mock).mockResolvedValue(mockSupabase);
    (headers as Mock).mockResolvedValue(mockHeaders());
    (redirect as Mock).mockImplementation(() => {
      throw new Error('Redirect called'); // Next.js redirects throw
    });
  });

  describe('signInWithSpotify', () => {
    it('should call Supabase OAuth with correct Spotify configuration', async () => {
      const mockUrl = 'https://spotify.oauth.url';
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: mockUrl },
        error: null,
      });

      await expect(signInWithSpotify()).rejects.toThrow('Redirect called');

      expect(mockSupabase.auth.signInWithOAuth).toHaveBeenCalledWith({
        provider: 'spotify',
        options: {
          scopes:
            'user-read-email playlist-read-private playlist-modify-private playlist-modify-public',
          redirectTo:
            'http://localhost:3000/api/auth/callback?platform=spotify',
        },
      });

      expect(redirect).toHaveBeenCalledWith(mockUrl);
    });

    it('should handle OAuth errors by redirecting to error page', async () => {
      const mockError = { message: 'OAuth failed' };
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: null },
        error: mockError,
      });

      await expect(signInWithSpotify()).rejects.toThrow('Redirect called');

      expect(redirect).toHaveBeenCalledWith(
        '/auth?error=Could not authenticate user'
      );
    });

    it('should use correct callback URL with platform parameter', async () => {
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: 'https://oauth.url' },
        error: null,
      });

      await expect(signInWithSpotify()).rejects.toThrow('Redirect called');

      expect(mockSupabase.auth.signInWithOAuth).toHaveBeenCalledWith(
        expect.objectContaining({
          options: expect.objectContaining({
            redirectTo:
              'http://localhost:3000/api/auth/callback?platform=spotify',
          }),
        })
      );
    });

    it('should handle different origin headers', async () => {
      mockHeaders().get.mockReturnValue('https://production.com');
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: 'https://oauth.url' },
        error: null,
      });

      await expect(signInWithSpotify()).rejects.toThrow('Redirect called');

      expect(mockSupabase.auth.signInWithOAuth).toHaveBeenCalledWith(
        expect.objectContaining({
          options: expect.objectContaining({
            redirectTo:
              'https://production.com/api/auth/callback?platform=spotify',
          }),
        })
      );
    });

    it('should handle missing OAuth URL in response', async () => {
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: null },
        error: null,
      });

      // Should not throw since no redirect occurs when url is null and no error
      await signInWithSpotify();

      expect(redirect).not.toHaveBeenCalled();
    });
  });
});

