import { API_BASE_URL } from '@/services/api';
import { NextRequest, NextResponse } from 'next/server';

export async function GET(request: NextRequest) {
  const searchParams = request.nextUrl.searchParams;
  const code = searchParams.get('code');
  const state = searchParams.get('state');
  const error = searchParams.get('error');

  // Handle Spotify errors
  if (error) {
    return NextResponse.redirect(
      new URL(`/auth?error=${encodeURIComponent(error)}`, request.url)
    );
  }

  // Validate parameters
  if (!code || !state) {
    return NextResponse.redirect(
      new URL('/auth?error=missing_parameters', request.url)
    );
  }

  try {
    // Call your backend API
    const response = await fetch(
      `${API_BASE_URL}/api/auth/callback?code=${code}&state=${state}`,
      {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
        credentials: 'include',
      }
    );

    if (!response.ok) {
      const errorText = await response.text();
      console.error('Backend auth failed:', errorText);
      return NextResponse.redirect(
        new URL('/auth?error=authentication_failed', request.url)
      );
    }

    const data = await response.json();
    console.log('Auth callback response:', data);

    // Create response with redirect
    const redirectResponse = NextResponse.redirect(
      new URL('/dashboard', request.url)
    );

    // Set the auth cookie (matching your backend's cookie settings)
    redirectResponse.cookies.set('rw-auth-token', data.token, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      path: '/',
      maxAge: 60 * 60 * 24, // 1 day
    });

    return redirectResponse;
  } catch (error) {
    console.error('Auth callback error:', error);
    return NextResponse.redirect(
      new URL('/auth?error=server_error', request.url)
    );
  }
}
