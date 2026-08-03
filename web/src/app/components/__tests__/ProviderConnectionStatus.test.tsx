import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { ProviderConnectionStatus } from '../ProviderConnectionStatus';
import {
  getConnectionStatus,
  storeProviderTokens,
} from '../../services/api';
import { useMusicKit } from '../../hooks/useMusicKit';
import { signInWithSpotify } from '../../auth/actions';

vi.mock('../../services/api', () => ({
  getConnectionStatus: vi.fn(),
  storeProviderTokens: vi.fn(),
}));

// Server action — invoked directly so a signed-in user isn't bounced off /auth.
vi.mock('../../auth/actions', () => ({
  signInWithSpotify: vi.fn(),
}));

vi.mock('../../hooks/useMusicKit', () => ({
  useMusicKit: vi.fn(),
}));

vi.mock('@/components/ui/ClientDate', () => ({
  ClientDate: ({ date }: { date: string }) => <span>{date}</span>,
}));

describe('ProviderConnectionStatus', () => {
  let mockAuthorize: Mock;

  beforeEach(() => {
    vi.clearAllMocks();
    mockAuthorize = vi.fn().mockResolvedValue('mut-token');
    (useMusicKit as Mock).mockReturnValue({
      ready: true,
      error: null,
      authorize: mockAuthorize,
    });
  });

  const connectedStatus = (overrides = {}) => ({
    provider: 'spotify',
    connected: true,
    connectedAt: '2026-01-01T00:00:00Z',
    canRefresh: true,
    ...overrides,
  });

  it('shows connected state for Spotify', async () => {
    (getConnectionStatus as Mock).mockResolvedValue(connectedStatus());

    render(<ProviderConnectionStatus provider="spotify" />);

    await waitFor(() =>
      expect(screen.getByText('Spotify Connected')).toBeInTheDocument()
    );
    expect(getConnectionStatus).toHaveBeenCalledWith('spotify');
    expect(screen.queryByText('Reconnect')).not.toBeInTheDocument();
  });

  it('starts Spotify OAuth via the server action, not a route to /auth', async () => {
    // /auth redirects any signed-in session straight back to the dashboard, so navigating
    // there makes connect/reconnect a silent no-op. The action goes to Spotify directly.
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({ connected: false })
    );
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="spotify" />);
    await waitFor(() =>
      expect(screen.getByText('Spotify Not Connected')).toBeInTheDocument()
    );

    await user.click(screen.getByRole('button', { name: /connect spotify/i }));

    expect(signInWithSpotify).toHaveBeenCalledOnce();
    expect(mockAuthorize).not.toHaveBeenCalled();
  });

  it('connects Apple Music through MusicKit and stores the Music User Token', async () => {
    (getConnectionStatus as Mock)
      .mockResolvedValueOnce(
        connectedStatus({ provider: 'apple_music', connected: false })
      )
      .mockResolvedValueOnce(
        connectedStatus({ provider: 'apple_music', connected: true, canRefresh: false })
      );
    (storeProviderTokens as Mock).mockResolvedValue({ success: true });
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Not Connected')).toBeInTheDocument()
    );

    await user.click(
      screen.getByRole('button', { name: /connect apple music/i })
    );

    await waitFor(() =>
      expect(screen.getByText('Apple Music Connected')).toBeInTheDocument()
    );
    expect(mockAuthorize).toHaveBeenCalledOnce();
    expect(storeProviderTokens).toHaveBeenCalledWith('apple_music', 'mut-token');
    expect(signInWithSpotify).not.toHaveBeenCalled();
  });

  it('shows a friendly error when Apple authorization fails (e.g. no subscription)', async () => {
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({ provider: 'apple_music', connected: false })
    );
    mockAuthorize.mockRejectedValue(new Error('user denied'));
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Not Connected')).toBeInTheDocument()
    );

    await user.click(
      screen.getByRole('button', { name: /connect apple music/i })
    );

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(screen.getByRole('alert')).toHaveTextContent(/subscription/i);
    expect(storeProviderTokens).not.toHaveBeenCalled();
    consoleSpy.mockRestore();
  });

  it('distinguishes a failed token store from a failed Apple authorization', async () => {
    // Apple granted the token; our own API rejected it. Blaming the subscription here would
    // send the user to renew one they already have.
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({ provider: 'apple_music', connected: false })
    );
    (storeProviderTokens as Mock).mockRejectedValue(
      new Error('API Error: 400 Bad Request')
    );
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Not Connected')).toBeInTheDocument()
    );

    await user.click(
      screen.getByRole('button', { name: /connect apple music/i })
    );

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(screen.getByRole('alert')).not.toHaveTextContent(/subscription/i);
    expect(screen.getByRole('alert')).toHaveTextContent(/saving it failed/i);
    // Authorization did happen — only the store failed.
    expect(mockAuthorize).toHaveBeenCalledOnce();
    consoleSpy.mockRestore();
  });

  it('does NOT prompt Apple reconnect just because canRefresh is false', async () => {
    // Apple has no refresh flow — canRefresh is always false. The Spotify heuristic
    // (connected && !canRefresh → Reconnect) must not carry over.
    const farExpiry = new Date(Date.now() + 100 * 24 * 3600 * 1000).toISOString();
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({
        provider: 'apple_music',
        connected: true,
        canRefresh: false,
        expiresAt: farExpiry,
      })
    );

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Connected')).toBeInTheDocument()
    );

    expect(screen.queryByText('Reconnect')).not.toBeInTheDocument();
  });

  it('prompts Apple reconnect when the token is close to expiry', async () => {
    const nearExpiry = new Date(Date.now() + 3 * 24 * 3600 * 1000).toISOString();
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({
        provider: 'apple_music',
        connected: true,
        canRefresh: false,
        expiresAt: nearExpiry,
      })
    );

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Connected')).toBeInTheDocument()
    );

    expect(screen.getByText('Reconnect')).toBeInTheDocument();
  });

  it('prompts Spotify reconnect when tokens cannot refresh, and reconnect re-runs OAuth', async () => {
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({ canRefresh: false })
    );
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="spotify" />);
    await waitFor(() =>
      expect(screen.getByText('Spotify Connected')).toBeInTheDocument()
    );

    // The reconnecting user is signed in — exactly the case where a /auth round-trip
    // would bounce back without doing anything.
    await user.click(screen.getByText('Reconnect'));
    expect(signInWithSpotify).toHaveBeenCalledOnce();
  });
});
