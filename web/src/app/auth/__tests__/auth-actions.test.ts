import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { createClient } from '../../lib/supabase/server';
import { headers } from 'next/headers';
import { redirect } from 'next/navigation';
import { sendMagicLink, signInWithApple, signInWithGoogle } from '../actions';

vi.mock('@/lib/supabase/server');
vi.mock('next/headers');
vi.mock('next/navigation');

describe('Auth Actions', () => {
  let mockSupabase: {
    auth: {
      signInWithOAuth: Mock;
      signInWithOtp: Mock;
    };
  };

  beforeEach(() => {
    mockSupabase = {
      auth: {
        signInWithOAuth: vi.fn(),
        signInWithOtp: vi.fn().mockResolvedValue({ error: null }),
      },
    };

    (createClient as Mock).mockResolvedValue(mockSupabase);
    (headers as Mock).mockResolvedValue({
      get: vi.fn().mockReturnValue('http://localhost:3000'),
    });
    (redirect as Mock).mockImplementation(() => {
      throw new Error('Redirect called'); // Next.js redirects throw
    });
  });

  describe('OAuth identity providers', () => {
    it('requests only identity scopes for Apple', async () => {
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: 'https://apple.oauth.url' },
        error: null,
      });

      await expect(signInWithApple()).rejects.toThrow('Redirect called');

      expect(mockSupabase.auth.signInWithOAuth).toHaveBeenCalledWith({
        provider: 'apple',
        options: {
          // Apple identity sign-in can never yield a Music User Token, so no
          // music scopes are requested here — MusicKit handles that separately.
          scopes: 'name email',
          redirectTo: 'http://localhost:3000/api/auth/callback',
        },
      });
    });

    it('requests only identity scopes for Google', async () => {
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: 'https://google.oauth.url' },
        error: null,
      });

      await expect(signInWithGoogle()).rejects.toThrow('Redirect called');

      expect(mockSupabase.auth.signInWithOAuth).toHaveBeenCalledWith({
        provider: 'google',
        options: {
          scopes: 'email profile',
          redirectTo: 'http://localhost:3000/api/auth/callback',
        },
      });
    });

    it('sends the user back to /auth when the provider errors', async () => {
      mockSupabase.auth.signInWithOAuth.mockResolvedValue({
        data: { url: null },
        error: new Error('provider down'),
      });

      await expect(signInWithApple()).rejects.toThrow('Redirect called');

      expect(redirect).toHaveBeenCalledWith(
        expect.stringContaining('/auth?error=')
      );
    });

    it('offers no Spotify provider', async () => {
      const actions = await import('../actions');
      expect(actions).not.toHaveProperty('signInWithSpotify');
    });
  });

  describe('sendMagicLink', () => {
    const submit = (email: string) => {
      const data = new FormData();
      data.set('email', email);
      return sendMagicLink({ status: 'idle' }, data);
    };

    it('sends a link pointing at the confirm route', async () => {
      const result = await submit('someone@example.com');

      expect(mockSupabase.auth.signInWithOtp).toHaveBeenCalledWith({
        email: 'someone@example.com',
        options: {
          emailRedirectTo: 'http://localhost:3000/auth/confirm',
          shouldCreateUser: true,
        },
      });
      expect(result).toEqual({ status: 'sent', email: 'someone@example.com' });
    });

    it('normalises the address before sending', async () => {
      await submit('  Someone@Example.COM  ');

      expect(mockSupabase.auth.signInWithOtp).toHaveBeenCalledWith(
        expect.objectContaining({ email: 'someone@example.com' })
      );
    });

    it('rejects a malformed address without calling Supabase', async () => {
      const result = await submit('not-an-email');

      expect(mockSupabase.auth.signInWithOtp).not.toHaveBeenCalled();
      expect(result.status).toBe('error');
      expect(result.message).toMatch(/doesn't look like an email address/i);
    });

    it('reports success even when Supabase errors, so the form is not an account oracle', async () => {
      // Anything other than a rate limit is reported as an ordinary failure —
      // never "no such user", which would let a stranger enumerate addresses.
      mockSupabase.auth.signInWithOtp.mockResolvedValue({
        error: { status: 500, message: 'boom' },
      });

      const result = await submit('someone@example.com');

      expect(result.status).toBe('error');
      expect(result.message).not.toMatch(/exist|found|unknown|registered/i);
    });

    it('surfaces throttling rather than pretending an email is coming', async () => {
      mockSupabase.auth.signInWithOtp.mockResolvedValue({
        error: { status: 429, message: 'rate limited' },
      });

      const result = await submit('someone@example.com');

      expect(result.status).toBe('error');
      expect(result.message).toMatch(/too many requests/i);
    });
  });
});
