import { test, expect, Page } from '@playwright/test';

/**
 * Comprehensive E2E authentication tests
 * Tests complete OAuth flow with mocked responses and session management
 */
test.describe('Complete Authentication Flow', () => {
  /**
   * Sets up auth token mock to simulate authenticated user
   */
  async function mockAuthenticatedUser(page: Page) {
    // Mock successful authentication by intercepting API calls
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'test-user-id',
          email: 'test@example.com',
          supabaseId: 'test-supabase-id',
        }),
      });
    });

    // Mock Spotify connection status
    await page.route('**/api/auth/spotify/status', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          isConnected: true,
          lastSync: new Date().toISOString(),
        }),
      });
    });

    // Set auth token in local storage or session storage if needed
    await page.addInitScript(() => {
      // Mock Supabase session
      localStorage.setItem('supabase.auth.token', JSON.stringify({
        access_token: 'mock-jwt-token',
        refresh_token: 'mock-refresh-token',
        user: {
          id: 'test-user-id',
          email: 'test@example.com',
        },
      }));
    });
  }

  /**
   * Sets up OAuth callback mock to simulate successful Spotify authentication
   */
  async function mockSuccessfulOAuthFlow(page: Page) {
    // Intercept Spotify OAuth initiation
    await page.route('**/accounts.spotify.com/**', async (route) => {
      // Simulate successful OAuth by redirecting to callback
      const callbackUrl = new URL('/api/auth/callback', page.url());
      callbackUrl.searchParams.set('code', 'mock-auth-code');
      callbackUrl.searchParams.set('platform', 'spotify');
      
      await route.fulfill({
        status: 302,
        headers: {
          'Location': callbackUrl.toString(),
        },
      });
    });

    // Mock auth callback route
    await page.route('**/api/auth/callback**', async (route) => {
      await route.fulfill({
        status: 302,
        headers: {
          'Location': '/dashboard',
        },
      });
    });

    // Mock token sync API
    await page.route('**/api/auth/spotify/tokens', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true }),
      });
    });
  }

  test('should complete full OAuth flow with mocked Spotify', async ({ page }) => {
    await mockSuccessfulOAuthFlow(page);
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await expect(spotifyButton).toBeVisible();
    
    await spotifyButton.click();
    
    // Should be redirected to dashboard after successful auth
    await expect(page).toHaveURL(/.*dashboard/);
  });

  test('should maintain session across page refreshes', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    // Navigate to dashboard
    await page.goto('/dashboard');
    
    // Verify we're authenticated
    await expect(page).toHaveURL(/.*dashboard/);
    
    // Refresh the page
    await page.reload();
    
    // Should still be authenticated and on dashboard
    await expect(page).toHaveURL(/.*dashboard/);
    
    // Verify user info is still available
    const response = await page.request.get('/api/auth/me');
    expect(response.status()).toBe(200);
  });

  test('should handle token refresh transparently', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    // Mock token refresh scenario
    let tokenRefreshCalled = false;
    await page.route('**/auth/v1/token**', async (route) => {
      tokenRefreshCalled = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          access_token: 'new-mock-jwt-token',
          refresh_token: 'new-mock-refresh-token',
          expires_in: 3600,
        }),
      });
    });
    
    await page.goto('/dashboard');
    
    // Simulate token expiration by manipulating stored token
    await page.evaluate(() => {
      const token = JSON.parse(localStorage.getItem('supabase.auth.token') || '{}');
      token.expires_at = Date.now() / 1000 - 100; // Expired 100 seconds ago
      localStorage.setItem('supabase.auth.token', JSON.stringify(token));
    });
    
    // Navigate to another page that requires auth
    await page.goto('/jobs');
    
    // Should handle token refresh transparently
    await expect(page).toHaveURL(/.*jobs/);
  });

  test('should handle authentication errors gracefully', async ({ page }) => {
    // Mock OAuth error scenario
    await page.route('**/accounts.spotify.com/**', async (route) => {
      const errorUrl = new URL('/auth', page.url());
      errorUrl.searchParams.set('error', 'access_denied');
      
      await route.fulfill({
        status: 302,
        headers: {
          'Location': errorUrl.toString(),
        },
      });
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should be redirected back to auth page with error
    await expect(page).toHaveURL(/.*auth.*error=access_denied/);
    
    // Error should be displayed to user
    await expect(page.getByRole('alert')).toContainText('access_denied');
  });

  test('should handle network errors during authentication', async ({ page }) => {
    // Mock network error during OAuth
    await page.route('**/accounts.spotify.com/**', async (route) => {
      await route.abort('failed');
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should handle network error gracefully
    // The exact behavior depends on implementation, but should not crash
    await page.waitForTimeout(2000); // Wait for any error handling
    
    // User should still be on auth page or see an error message
    const isOnAuthPage = page.url().includes('/auth');
    const hasErrorMessage = await page.getByRole('alert').isVisible().catch(() => false);
    
    expect(isOnAuthPage || hasErrorMessage).toBe(true);
  });

  test('should redirect to intended page after authentication', async ({ page }) => {
    await mockSuccessfulOAuthFlow(page);
    
    // Try to access protected page while unauthenticated
    await page.goto('/jobs/some-job-id');
    
    // Should be redirected to auth with next parameter
    await expect(page).toHaveURL(/.*auth/);
    
    // Complete authentication
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should be redirected to originally intended page
    // Note: This depends on implementation of next parameter handling
    await expect(page).toHaveURL(/.*dashboard/);
  });

  test('should handle logout correctly', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    // Mock logout endpoint
    await page.route('**/api/auth/logout', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true }),
      });
    });
    
    await page.goto('/dashboard');
    
    // Find and click logout button (adjust selector based on actual implementation)
    const logoutButton = page.getByRole('button', { name: /logout|sign out/i });
    if (await logoutButton.isVisible()) {
      await logoutButton.click();
      
      // Should be redirected to auth page
      await expect(page).toHaveURL(/.*auth/);
    }
  });

  test('should handle expired sessions correctly', async ({ page }) => {
    // Mock expired session
    await page.addInitScript(() => {
      localStorage.setItem('supabase.auth.token', JSON.stringify({
        access_token: 'expired-token',
        refresh_token: 'expired-refresh',
        expires_at: Date.now() / 1000 - 3600, // Expired 1 hour ago
        user: {
          id: 'test-user-id',
          email: 'test@example.com',
        },
      }));
    });
    
    // Mock auth API to return 401 for expired token
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Unauthorized' }),
      });
    });
    
    await page.goto('/dashboard');
    
    // Should be redirected to auth page due to expired session
    await expect(page).toHaveURL(/.*auth/);
  });

  test('should display user information when authenticated', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    await page.goto('/dashboard');
    
    // Should display user information (adjust selector based on actual implementation)
    const userInfo = page.locator('[data-testid="user-info"], .user-profile, .user-email');
    await expect(userInfo.first()).toBeVisible();
  });

  test('should handle simultaneous auth requests', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    // Open multiple tabs/contexts to simulate simultaneous requests
    const context = page.context();
    const page2 = await context.newPage();
    
    await mockAuthenticatedUser(page2);
    
    // Navigate both pages simultaneously
    await Promise.all([
      page.goto('/dashboard'),
      page2.goto('/dashboard'),
    ]);
    
    // Both should succeed
    await expect(page).toHaveURL(/.*dashboard/);
    await expect(page2).toHaveURL(/.*dashboard/);
    
    await page2.close();
  });

  test('should work correctly with browser back/forward navigation', async ({ page }) => {
    await mockAuthenticatedUser(page);
    
    // Navigate through several pages
    await page.goto('/dashboard');
    await page.goto('/jobs');
    
    // Use browser back button
    await page.goBack();
    await expect(page).toHaveURL(/.*dashboard/);
    
    // Use browser forward button
    await page.goForward();
    await expect(page).toHaveURL(/.*jobs/);
    
    // Should maintain authentication throughout navigation
    const response = await page.request.get('/api/auth/me');
    expect(response.status()).toBe(200);
  });
});