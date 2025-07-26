import { test, expect, Page } from '@playwright/test';

test.describe('Create Clean Version - Server-Client Boundary', () => {
  
  let page: Page;

  test.beforeEach(async ({ page: testPage }) => {
    page = testPage;
    
    // Navigate to the app (it should redirect to auth if not authenticated)
    await page.goto('/dashboard');
    
    // For now, we'll assume authentication is handled separately
    // In a real scenario, you'd handle the full auth flow here
    await page.waitForLoadState('networkidle');
  });

  test('should reproduce server-client boundary error in browser', async () => {
    // Listen for console errors
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    // Listen for unhandled exceptions
    const pageErrors: Error[] = [];
    page.on('pageerror', (error) => {
      pageErrors.push(error);
    });

    // Track network requests to verify API isn't called
    const networkRequests: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/cleanplaylist')) {
        networkRequests.push(request.url());
      }
    });

    try {
      // Verify page loaded correctly (may redirect to auth)
      await page.waitForSelector('h1', { timeout: 10000 });
      
      const pageTitle = await page.locator('h1').textContent();
      if (pageTitle?.includes('RadioWash')) {
        // We're on the dashboard
        await expect(page.locator('h2')).toContainText('Create a Clean Playlist');

        // Wait for playlists to load (if any)
        await page.waitForSelector('select', { state: 'visible', timeout: 5000 });
        
        // Check if we have playlist options
        const options = await page.locator('select option').count();
        
        if (options > 1) { // More than just the default "Choose a playlist" option
          // Select a playlist
          await page.selectOption('select', { index: 1 });
          
          // Verify button becomes enabled
          const createButton = page.locator('button', { hasText: 'Create Clean Version' });
          await expect(createButton).toBeEnabled();

          // Click the button that should trigger the error
          await createButton.click();

          // Wait for potential error to occur
          await page.waitForTimeout(3000);

          // Check for the specific serialization error
          const hasSerializationError = consoleErrors.some(error => 
            error.includes('Only plain objects, and a few built-ins, can be passed to Client Components from Server Components') ||
            error.includes('4187410481')
          );

          const hasOtherRelevantError = consoleErrors.some(error => 
            error.includes('Converting circular structure to JSON') ||
            error.includes('serialization') ||
            error.includes('circular')
          );

          // Log all errors for debugging
          if (consoleErrors.length > 0) {
            console.log('Console errors captured:', consoleErrors);
          }
          if (pageErrors.length > 0) {
            console.log('Page errors captured:', pageErrors.map(e => e.message));
          }

          // Current state: should have errors (this test should fail initially)
          expect(hasSerializationError || hasOtherRelevantError).toBe(true);
          
          // Verify no actual API call was made (error happens before)
          expect(networkRequests.length).toBe(0);
        } else {
          // No playlists available, skip this test
          test.skip('No playlists available for testing');
        }
      } else {
        // We're probably on the auth page
        test.skip('Not authenticated - redirected to auth page');
      }
    } catch (error) {
      console.log('Test setup error:', error);
      // Take a screenshot for debugging
      await page.screenshot({ path: 'test-error-screenshot.png', fullPage: true });
      throw error;
    }
  });

  test('should work correctly after fix is implemented', async () => {
    // This test will pass after we implement the fix
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    // Mock successful API response for when fix is implemented
    await page.route('**/api/cleanplaylist/**', async (route) => {
      const json = {
        id: 1,
        sourcePlaylistId: 'playlist123',
        sourcePlaylistName: 'Test Playlist',
        status: 'pending',
        totalTracks: 10,
        processedTracks: 0,
        matchedTracks: 0,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      await route.fulfill({ 
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(json)
      });
    });

    try {
      // Navigate and wait for page load
      await page.goto('/dashboard');
      await page.waitForLoadState('networkidle');
      
      // Check if we're on the dashboard
      const pageTitle = await page.locator('h1').textContent();
      if (pageTitle?.includes('RadioWash')) {
        await page.waitForSelector('select', { state: 'visible', timeout: 5000 });
        
        const options = await page.locator('select option').count();
        
        if (options > 1) {
          await page.selectOption('select', { index: 1 });
          
          const createButton = page.locator('button', { hasText: 'Create Clean Version' });
          await createButton.click();

          // Wait for mutation to complete
          await page.waitForTimeout(2000);

          // After fix: should have no serialization errors
          const hasSerializationError = consoleErrors.some(error => 
            error.includes('Only plain objects, and a few built-ins, can be passed to Client Components')
          );
          
          expect(hasSerializationError).toBe(false);

          // Should see success state (button text changes)
          await expect(page.locator('text=Working on it...')).toBeVisible({ timeout: 5000 });
          
          // Job should appear in the job list (if we have a job list component)
          // This would be added once we know the exact structure
        } else {
          test.skip('No playlists available for testing');
        }
      } else {
        test.skip('Not authenticated - redirected to auth page');
      }
    } catch (error) {
      console.log('Test error:', error);
      await page.screenshot({ path: 'test-after-fix-error.png', fullPage: true });
      throw error;
    }
  });

  test('should handle network errors gracefully', async () => {
    // Mock network failure
    await page.route('**/api/cleanplaylist/**', async (route) => {
      await route.abort('internetdisconnected');
    });

    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    try {
      await page.goto('/dashboard');
      await page.waitForLoadState('networkidle');
      
      const pageTitle = await page.locator('h1').textContent();
      if (pageTitle?.includes('RadioWash')) {
        await page.waitForSelector('select', { state: 'visible', timeout: 5000 });
        
        const options = await page.locator('select option').count();
        if (options > 1) {
          await page.selectOption('select', { index: 1 });
          
          const createButton = page.locator('button', { hasText: 'Create Clean Version' });
          await createButton.click();

          // Should handle error gracefully without serialization issues
          await expect(page.locator('text=Working on it...')).toBeVisible({ timeout: 5000 });
          
          // Wait for error handling
          await page.waitForTimeout(3000);
          
          // Should not have serialization errors, only network errors
          const hasSerializationError = consoleErrors.some(error => 
            error.includes('Only plain objects, and a few built-ins, can be passed to Client Components')
          );
          
          expect(hasSerializationError).toBe(false);
        } else {
          test.skip('No playlists available for testing');
        }
      } else {
        test.skip('Not authenticated - redirected to auth page');
      }
    } catch (error) {
      console.log('Network error test failed:', error);
      await page.screenshot({ path: 'test-network-error.png', fullPage: true });
      throw error;
    }
  });

  test('should navigate to dashboard and check basic functionality', async () => {
    // Basic smoke test to ensure app loads
    await page.goto('/dashboard');
    await page.waitForLoadState('networkidle');
    
    // Take screenshot for debugging
    await page.screenshot({ path: 'dashboard-loaded.png', fullPage: true });
    
    // Check if page loaded (either dashboard or auth)
    const hasRadioWash = await page.locator('h1:has-text("RadioWash")').count() > 0;
    const hasAuth = await page.locator('text=Sign in').count() > 0;
    
    expect(hasRadioWash || hasAuth).toBe(true);
    
    if (hasRadioWash) {
      console.log('Successfully loaded dashboard');
      // Check for key elements
      await expect(page.locator('h1')).toContainText('RadioWash');
    } else {
      console.log('Redirected to auth page');
    }
  });
});