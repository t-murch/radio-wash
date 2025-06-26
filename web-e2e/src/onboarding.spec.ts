import { test, expect } from '@playwright/test';

test.describe('Onboarding Flow', () => {
  test.beforeEach(async ({ page }) => {
    // Mock the auth API responses
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        json: {
          id: 1,
          supabaseUserId: '550e8400-e29b-41d4-a716-446655440001',
          displayName: 'Test User',
          email: 'test@example.com'
        }
      });
    });

    await page.route('**/api/musicservice/connected', async route => {
      await route.fulfill({
        json: [] // No connected services initially
      });
    });
  });

  test('should redirect unauthenticated users to auth page', async ({ page }) => {
    // Mock unauthenticated state
    await page.route('**/api/auth/me', async route => {
      await route.fulfill({
        status: 401,
        json: { error: 'Unauthorized' }
      });
    });

    await page.goto('/onboarding');
    
    // Should redirect to auth page
    await expect(page).toHaveURL('/auth');
  });

  test('should display welcome step for authenticated user without music services', async ({ page }) => {
    await page.goto('/onboarding');
    
    // Should show welcome step
    await expect(page.locator('h2')).toContainText('Welcome to RadioWash!');
    await expect(page.locator('text=Hi Test User!')).toBeVisible();
    
    // Progress indicators should show first step as current
    const progressSteps = page.locator('[class*="rounded-full"]').first();
    await expect(progressSteps).toHaveClass(/bg-blue-500/);
  });

  test('should auto-advance from welcome to music service setup', async ({ page }) => {
    await page.goto('/onboarding');
    
    // Wait for auto-advancement to music service step
    await expect(page.locator('h2')).toContainText('Connect Your Music Service', { timeout: 3000 });
    
    // Should show music service options
    await expect(page.locator('text=Spotify')).toBeVisible();
    await expect(page.locator('text=Apple Music')).toBeVisible();
    await expect(page.locator('text=Coming Soon')).toBeVisible();
  });

  test('should show error message when connection fails', async ({ page }) => {
    await page.goto('/onboarding?error=spotify_connection_failed');
    
    // Should display error message
    await expect(page.locator('[role="alert"], .bg-red-50')).toContainText('Failed to connect to Spotify');
    
    // Error should have proper styling
    await expect(page.locator('.text-red-700, .text-red-300')).toBeVisible();
  });

  test('should handle Spotify connection flow', async ({ page }) => {
    await page.goto('/onboarding');
    
    // Wait for music service step
    await expect(page.locator('text=Connect Your Music Service')).toBeVisible();
    
    // Mock Spotify auth redirect
    await page.route('**/api/musicservice/spotify/auth', async route => {
      // Simulate redirect to Spotify OAuth
      await route.fulfill({
        status: 302,
        headers: {
          'Location': 'https://accounts.spotify.com/authorize?...'
        }
      });
    });
    
    // Click Spotify connect button
    const spotifyButton = page.locator('text=Connect').first();
    await expect(spotifyButton).toBeVisible();
    
    // Clicking should trigger navigation (we'll mock the full flow)
    await spotifyButton.click();
  });

  test('should show completion step when music service is connected', async ({ page }) => {
    // Mock connected services response
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

    await page.goto('/onboarding');
    
    // Should skip to completion step
    await expect(page.locator('h2')).toContainText("You're All Set!", { timeout: 3000 });
    await expect(page.locator('text=Perfect! Your account is all set up')).toBeVisible();
    
    // Should show "Go to Dashboard" button
    await expect(page.locator('text=Go to Dashboard Now')).toBeVisible();
  });

  test('should redirect to dashboard when onboarding is complete', async ({ page }) => {
    // Mock connected services
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

    await page.goto('/onboarding');
    
    // Wait for completion step and click dashboard button
    await expect(page.locator('text=Go to Dashboard Now')).toBeVisible();
    
    // Mock dashboard route
    await page.route('/dashboard', async route => {
      await route.fulfill({
        status: 200,
        body: '<html><body>Dashboard</body></html>'
      });
    });
    
    await page.locator('text=Go to Dashboard Now').click();
    await expect(page).toHaveURL('/dashboard');
  });

  test('should display connected service status correctly', async ({ page }) => {
    // Start with no services
    await page.goto('/onboarding');
    await expect(page.locator('text=Connect Your Music Service')).toBeVisible();
    
    // Initially should show "Connect" button for Spotify
    await expect(page.locator('text=Connect').first()).toBeVisible();
    
    // Mock service becoming connected
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
    
    // Simulate page refresh or service update
    await page.reload();
    
    // Should now show "Connected" status
    await expect(page.locator('text=Connected')).toBeVisible();
    await expect(page.locator('[class*="text-green-"]')).toBeVisible();
  });

  test('should be responsive and accessible', async ({ page }) => {
    await page.goto('/onboarding');
    
    // Test mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Content should still be visible and properly formatted
    await expect(page.locator('h1')).toContainText('Account Setup');
    await expect(page.locator('h2')).toBeVisible();
    
    // Progress indicators should be visible
    await expect(page.locator('[class*="rounded-full"]')).toHaveCount(3);
    
    // Test keyboard navigation
    await page.keyboard.press('Tab');
    await expect(page.locator(':focus')).toBeVisible();
  });

  test('should handle network errors gracefully', async ({ page }) => {
    // Mock network failure for music services API
    await page.route('**/api/musicservice/connected', async route => {
      await route.abort('connectionfailed');
    });

    await page.goto('/onboarding');
    
    // Should still render the page without crashing
    await expect(page.locator('h1')).toContainText('Account Setup');
    
    // Should handle the error gracefully (might show loading state or error message)
    // The exact behavior depends on how the AuthContext handles API failures
  });
});