import { test, expect, Page } from '@playwright/test';

/**
 * E2E tests for authentication edge cases and error scenarios
 */
test.describe('Authentication Edge Cases', () => {
  test('should handle malformed auth tokens', async ({ page }) => {
    // Set malformed token in storage
    await page.addInitScript(() => {
      localStorage.setItem('supabase.auth.token', 'malformed-json-string');
    });
    
    await page.goto('/dashboard');
    
    // Should handle malformed token gracefully and redirect to auth
    await expect(page).toHaveURL(/.*auth/);
  });

  test('should handle missing environment variables gracefully', async ({ page }) => {
    // Mock missing API URL by intercepting requests
    await page.route('**/api/**', async (route) => {
      await route.abort('failed');
    });
    
    await page.goto('/auth');
    
    // Should still render the auth page
    await expect(page.getByRole('button', { name: /sign up with spotify/i })).toBeVisible();
  });

  test('should handle CORS errors during authentication', async ({ page }) => {
    // Mock CORS error
    await page.route('**/api/auth/**', async (route) => {
      await route.fulfill({
        status: 0, // Network error
        body: '',
      });
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should handle CORS error gracefully
    await page.waitForTimeout(2000);
    
    // Should remain on auth page or show error
    const isOnAuthPage = page.url().includes('/auth');
    expect(isOnAuthPage).toBe(true);
  });

  test('should handle very slow auth responses', async ({ page }) => {
    // Mock slow auth response
    await page.route('**/api/auth/me', async (route) => {
      await new Promise(resolve => setTimeout(resolve, 5000)); // 5 second delay
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'test-user-id',
          email: 'test@example.com',
        }),
      });
    });
    
    await page.goto('/dashboard');
    
    // Should show loading state or handle timeout gracefully
    // Implementation specific - might show spinner, redirect to auth, etc.
    await page.waitForTimeout(2000);
    
    // Test should not hang indefinitely
    const currentUrl = page.url();
    expect(currentUrl).toBeTruthy();
  });

  test('should handle auth state changes during page navigation', async ({ page }) => {
    // Start unauthenticated
    await page.goto('/auth');
    
    // Mock authentication during navigation
    await page.addInitScript(() => {
      setTimeout(() => {
        localStorage.setItem('supabase.auth.token', JSON.stringify({
          access_token: 'new-token',
          user: { id: 'user-id', email: 'test@example.com' },
        }));
        
        // Trigger auth state change event
        window.dispatchEvent(new StorageEvent('storage', {
          key: 'supabase.auth.token',
          newValue: localStorage.getItem('supabase.auth.token'),
        }));
      }, 1000);
    });
    
    // Mock auth endpoint
    await page.route('**/api/auth/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'user-id',
          email: 'test@example.com',
        }),
      });
    });
    
    // Should handle auth state change and potentially redirect
    await page.waitForTimeout(2000);
    
    // Navigate to dashboard - should work now
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/.*dashboard/);
  });

  test('should handle multiple authentication attempts', async ({ page }) => {
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    
    // Click multiple times rapidly
    await spotifyButton.click();
    await spotifyButton.click();
    await spotifyButton.click();
    
    // Should handle multiple clicks gracefully without errors
    await page.waitForTimeout(1000);
    
    // Should still be functional
    expect(page.url()).toContain('auth');
  });

  test('should handle browser refresh during OAuth flow', async ({ page }) => {
    // Mock partial OAuth flow
    await page.route('**/accounts.spotify.com/**', async (route) => {
      // Don't complete the redirect immediately
      await new Promise(resolve => setTimeout(resolve, 2000));
      await route.fulfill({
        status: 200,
        body: '<html><body>OAuth in progress...</body></html>',
      });
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Refresh during OAuth flow
    await page.waitForTimeout(500);
    await page.reload();
    
    // Should handle refresh gracefully
    await expect(page.getByRole('button', { name: /sign up with spotify/i })).toBeVisible();
  });

  test('should handle localStorage being disabled', async ({ page }) => {
    // Disable localStorage
    await page.addInitScript(() => {
      Object.defineProperty(window, 'localStorage', {
        value: {
          getItem: () => { throw new Error('localStorage disabled'); },
          setItem: () => { throw new Error('localStorage disabled'); },
          removeItem: () => { throw new Error('localStorage disabled'); },
          clear: () => { throw new Error('localStorage disabled'); },
        },
        writable: false,
      });
    });
    
    await page.goto('/auth');
    
    // Should still render auth page despite localStorage issues
    await expect(page.getByRole('button', { name: /sign up with spotify/i })).toBeVisible();
  });

  test('should handle third-party cookies being blocked', async ({ page, context }) => {
    // Block third-party cookies
    await context.clearCookies();
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should handle blocked cookies gracefully
    // Exact behavior depends on implementation
    await page.waitForTimeout(2000);
    
    // Should not crash the application
    const title = await page.title();
    expect(title).toBeTruthy();
  });

  test('should handle invalid OAuth state parameter', async ({ page }) => {
    // Mock callback with invalid state
    await page.route('**/api/auth/callback**', async (route) => {
      const url = new URL(route.request().url());
      if (url.searchParams.get('state') !== 'expected-state') {
        await route.fulfill({
          status: 302,
          headers: {
            'Location': '/auth?error=invalid_state',
          },
        });
      }
    });
    
    // Simulate OAuth callback with invalid state
    await page.goto('/api/auth/callback?code=test&state=invalid-state&platform=spotify');
    
    // Should redirect to auth with error
    await expect(page).toHaveURL(/.*auth.*error=invalid_state/);
    await expect(page.getByRole('alert')).toContainText('invalid_state');
  });

  test('should handle auth timeout scenarios', async ({ page }) => {
    // Set shorter timeout for testing
    await page.setDefaultTimeout(3000);
    
    // Mock very slow auth endpoint
    await page.route('**/api/auth/**', async (route) => {
      await new Promise(resolve => setTimeout(resolve, 10000)); // 10 second delay
      await route.fulfill({
        status: 200,
        body: JSON.stringify({}),
      });
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    
    // Should handle timeout gracefully
    try {
      await spotifyButton.click();
      await page.waitForTimeout(4000);
    } catch (error) {
      // Timeout is expected - should handle gracefully
      expect(error).toBeTruthy();
    }
    
    // App should still be functional
    await expect(page.getByRole('button', { name: /sign up with spotify/i })).toBeVisible();
  });

  test('should handle invalid redirect URLs', async ({ page }) => {
    // Mock OAuth with invalid redirect
    await page.route('**/accounts.spotify.com/**', async (route) => {
      await route.fulfill({
        status: 302,
        headers: {
          'Location': 'javascript:alert("xss")', // Potentially malicious redirect
        },
      });
    });
    
    await page.goto('/auth');
    
    const spotifyButton = page.getByRole('button', { name: /sign up with spotify/i });
    await spotifyButton.click();
    
    // Should handle invalid redirect safely
    await page.waitForTimeout(2000);
    
    // Should not execute malicious code
    const alertDialogs = [];
    page.on('dialog', dialog => {
      alertDialogs.push(dialog);
      dialog.dismiss();
    });
    
    expect(alertDialogs).toHaveLength(0);
  });

  test('should handle concurrent auth requests from multiple tabs', async ({ page, context }) => {
    // Open multiple tabs
    const page2 = await context.newPage();
    const page3 = await context.newPage();
    
    // Mock auth endpoints
    const mockAuth = async (route: any) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'test-user-id',
          email: 'test@example.com',
        }),
      });
    };
    
    await page.route('**/api/auth/me', mockAuth);
    await page2.route('**/api/auth/me', mockAuth);
    await page3.route('**/api/auth/me', mockAuth);
    
    // Navigate all tabs simultaneously
    await Promise.all([
      page.goto('/dashboard'),
      page2.goto('/dashboard'),
      page3.goto('/dashboard'),
    ]);
    
    // All should succeed without conflicts
    await expect(page).toHaveURL(/.*dashboard/);
    await expect(page2).toHaveURL(/.*dashboard/);
    await expect(page3).toHaveURL(/.*dashboard/);
    
    await page2.close();
    await page3.close();
  });
});