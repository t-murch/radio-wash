import { test, expect } from '@playwright/test';

test.describe('Authentication Flow', () => {
  test('should redirect unauthenticated user to auth page', async ({
    page,
  }) => {
    // Navigate directly to the dashboard
    await page.goto('/dashboard');

    // Expect to be redirected to the /auth page
    await expect(page).toHaveURL(/.*auth/);
    await expect(
      page.getByRole('button', { name: /connect with spotify/i })
    ).toBeVisible();
  });

  test('should allow a user to initiate login with Spotify', async ({
    page,
  }) => {
    await page.goto('/auth');

    // We can't fully complete the OAuth flow in an E2E test without credentials,
    // but we can test that our app correctly initiates the flow by redirecting to Spotify.

    // Get the button and click it
    const spotifyButton = page.getByRole('button', {
      name: /connect with spotify/i,
    });
    await spotifyButton.click();

    // After clicking, we expect the page URL to change to something containing 'accounts.spotify.com'
    // We use a less strict expectation because the exact URL can change.
    await page.waitForURL('**/accounts.spotify.com/**');

    // Assert that the new URL is indeed the Spotify login page
    expect(page.url()).toContain('accounts.spotify.com');
  });

  // To test the authenticated state, you would typically use a method to
  // programmatically log in by setting auth cookies/tokens before the test runs.
  // This is an advanced technique that bypasses the UI login flow for speed and reliability.
  // For now, this test demonstrates the initial, unauthenticated part of the flow.
});
