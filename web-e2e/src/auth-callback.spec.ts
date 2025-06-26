import { test, expect } from '@playwright/test';

test.describe('Auth Callback Flow', () => {
  test('should redirect to onboarding when requiresMusicServiceSetup is true', async ({ page }) => {
    // Mock user without music services
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'New User',
          email: 'newuser@example.com'
        }
      });
    });

    await page.route('**/api/musicservice/connected', async route => {
      await route.fulfill({
        json: [] // No connected services
      });
    });

    await page.goto('/auth/callback');
    
    // Should redirect to onboarding
    await expect(page).toHaveURL('/onboarding');
  });

  test('should redirect to dashboard when user has connected services', async ({ page }) => {
    // Mock user with connected services
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'Existing User',
          email: 'existing@example.com'
        }
      });
    });

    await page.route('**/api/musicservice/connected', async route => {
      await route.fulfill({
        json: [
          {
            id: 1,
            serviceType: 'Spotify',
            serviceUserId: 'spotify-user-123',
            isActive: true,
            createdAt: new Date().toISOString()
          }
        ]
      });
    });

    // Mock dashboard route
    await page.route('/dashboard', async route => {
      await route.fulfill({
        status: 200,
        body: '<html><body>Dashboard</body></html>'
      });
    });

    await page.goto('/auth/callback');
    
    // Should redirect to dashboard
    await expect(page).toHaveURL('/dashboard');
  });

  test('should handle Spotify connection success callback', async ({ page }) => {
    // Mock successful Spotify connection
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'User',
          email: 'user@example.com'
        }
      });
    });

    await page.route('**/api/musicservice/connected', async route => {
      await route.fulfill({
        json: [
          {
            id: 1,
            serviceType: 'Spotify',
            serviceUserId: 'spotify-user-123',
            isActive: true,
            createdAt: new Date().toISOString()
          }
        ]
      });
    });

    // Mock dashboard route
    await page.route('/dashboard', async route => {
      await route.fulfill({
        status: 200,
        body: '<html><body>Dashboard</body></html>'
      });
    });

    await page.goto('/auth/callback?spotify_connected=true');
    
    // Should redirect to dashboard after successful connection
    await expect(page).toHaveURL('/dashboard');
  });

  test('should handle connection error callback', async ({ page }) => {
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'User',
          email: 'user@example.com'
        }
      });
    });

    await page.route('**/api/musicservice/connected', async route => {
      await route.fulfill({
        json: [] // Still no services after failed connection
      });
    });

    await page.goto('/auth/callback?error=spotify_connection_failed');
    
    // Should redirect to onboarding with error
    await expect(page).toHaveURL('/onboarding?error=spotify_connection_failed');
  });

  test('should show loading state during auth processing', async ({ page }) => {
    // Add delay to API response to test loading state
    await page.route('**/api/auth/me', async route => {
      await new Promise(resolve => setTimeout(resolve, 1000));
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'User',
          email: 'user@example.com'
        }
      });
    });

    await page.goto('/auth/callback');
    
    // Should show loading spinner
    await expect(page.locator('.animate-spin')).toBeVisible();
    await expect(page.locator('text=Finalizing authentication')).toBeVisible();
  });
});