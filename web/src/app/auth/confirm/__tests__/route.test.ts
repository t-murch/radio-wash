import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
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

const request = (url: string) => ({ url }) as never;

describe('Magic link confirm route', () => {
  let mockSupabase: { auth: { verifyOtp: Mock } };

  beforeEach(() => {
    mockSupabase = {
      auth: { verifyOtp: vi.fn().mockResolvedValue({ error: null }) },
    };
    (createClient as Mock).mockResolvedValue(mockSupabase);
    vi.clearAllMocks();
  });

  it('verifies the token hash and starts onboarding', async () => {
    await GET(
      request(
        'https://radiowash.com/auth/confirm?token_hash=hash123&type=magiclink'
      )
    );

    expect(mockSupabase.auth.verifyOtp).toHaveBeenCalledWith({
      type: 'magiclink',
      token_hash: 'hash123',
    });
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('sends an expired or already-used link to the recovery screen', async () => {
    mockSupabase.auth.verifyOtp.mockResolvedValue({
      error: { message: 'Token has expired or is invalid' },
    });

    await GET(
      request(
        'https://radiowash.com/auth/confirm?token_hash=stale&type=magiclink'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/auth/link-expired'
    );
  });

  it('treats a link with no token hash as unusable', async () => {
    await GET(request('https://radiowash.com/auth/confirm'));

    expect(mockSupabase.auth.verifyOtp).not.toHaveBeenCalled();
    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/auth/link-expired?reason=malformed'
    );
  });

  it('refuses an absolute next parameter so a sign-in link cannot be weaponised', async () => {
    await GET(
      request(
        'https://radiowash.com/auth/confirm?token_hash=h&type=magiclink&next=https://evil.example.com'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/onboarding'
    );
  });

  it('honours a relative next parameter', async () => {
    await GET(
      request(
        'https://radiowash.com/auth/confirm?token_hash=h&type=magiclink&next=/dashboard'
      )
    );

    expect(NextResponse.redirect).toHaveBeenCalledWith(
      'https://radiowash.com/dashboard'
    );
  });
});
