import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';

import { OnboardingClient } from '../onboarding-client';
import { useMusicKit } from '@/hooks/useMusicKit';
import { storeProviderTokens } from '@/services/api';

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));
vi.mock('@/hooks/useMusicKit', () => ({ useMusicKit: vi.fn() }));
vi.mock('@/services/api', () => ({ storeProviderTokens: vi.fn() }));

const musicKit = (over: Partial<ReturnType<typeof useMusicKit>> = {}) => ({
  ready: true,
  error: null,
  authorize: vi.fn().mockResolvedValue('music-user-token'),
  unauthorize: vi.fn(),
  ...over,
});

describe('Onboarding — connect Apple Music', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    (useMusicKit as Mock).mockReturnValue(musicKit());
    (storeProviderTokens as Mock).mockResolvedValue({ success: true });
  });

  it('explains why a second permission is needed before showing the prompt', () => {
    render(<OnboardingClient email="a@b.com" appleConnected={false} />);

    expect(
      screen.getByRole('heading', { name: /one more step: connect apple music/i })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/reading your playlists takes a separate permission/i)
    ).toBeInTheDocument();
    // The user should know the originals are safe before granting write access.
    expect(
      screen.getByText(/never edits or deletes your originals/i)
    ).toBeInTheDocument();
  });

  it('stores the token and moves to picking a playlist on success', async () => {
    const user = userEvent.setup();
    render(<OnboardingClient email="a@b.com" appleConnected={false} />);

    await user.click(screen.getByRole('button', { name: /connect apple music/i }));

    await waitFor(() =>
      expect(storeProviderTokens).toHaveBeenCalledWith(
        'apple_music',
        'music-user-token'
      )
    );
    expect(
      await screen.findByRole('heading', { name: /apple music is connected/i })
    ).toBeInTheDocument();
  });

  it('treats a declined authorization as a subscription requirement, not a user error', async () => {
    const user = userEvent.setup();
    (useMusicKit as Mock).mockReturnValue(
      musicKit({ authorize: vi.fn().mockRejectedValue(new Error('denied')) })
    );

    render(<OnboardingClient email="a@b.com" appleConnected={false} />);
    await user.click(screen.getByRole('button', { name: /connect apple music/i }));

    expect(
      await screen.findByText(/no active apple music subscription/i)
    ).toBeInTheDocument();
    // Apple never issued a token, so nothing should have been sent to our API.
    expect(storeProviderTokens).not.toHaveBeenCalled();
  });

  it('distinguishes our own storage failure from a missing subscription', async () => {
    const user = userEvent.setup();
    (storeProviderTokens as Mock).mockRejectedValue(new Error('500'));

    render(<OnboardingClient email="a@b.com" appleConnected={false} />);
    await user.click(screen.getByRole('button', { name: /connect apple music/i }));

    // Telling this person to check their subscription would send them to fix
    // something that is not broken.
    expect(
      await screen.findByText(/couldn't save it/i)
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/no active apple music subscription/i)
    ).not.toBeInTheDocument();
  });

  it('surfaces a MusicKit outage rather than leaving the button silently dead', () => {
    (useMusicKit as Mock).mockReturnValue(
      musicKit({ ready: false, error: 'script blocked' })
    );

    render(<OnboardingClient email="a@b.com" appleConnected={false} />);

    expect(screen.getByText(/isn't reachable right now/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /connect apple music/i })
    ).toBeDisabled();
  });

  it('skips straight to the playlist step when Apple is already connected', () => {
    render(<OnboardingClient email="a@b.com" appleConnected />);

    expect(
      screen.getByRole('heading', { name: /apple music is connected/i })
    ).toBeInTheDocument();
  });
});
