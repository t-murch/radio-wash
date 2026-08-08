import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { ProviderConnectionStatus } from '../ProviderConnectionStatus';
import {
  disconnectProvider,
  getConnectionStatus,
  storeProviderTokens,
} from '../../services/api';
import { useMusicKit } from '../../hooks/useMusicKit';

vi.mock('../../services/api', () => ({
  getConnectionStatus: vi.fn(),
  storeProviderTokens: vi.fn(),
  disconnectProvider: vi.fn(),
}));

// Server action — invoked directly so a signed-in user isn't bounced off /auth.
vi.mock('../../auth/actions', () => ({
}));

vi.mock('../../hooks/useMusicKit', () => ({
  useMusicKit: vi.fn(),
}));

vi.mock('@/components/ui/ClientDate', () => ({
  ClientDate: ({ date }: { date: string }) => <span>{date}</span>,
}));

describe('ProviderConnectionStatus', () => {
  let mockAuthorize: Mock;
  let mockUnauthorize: Mock;

  beforeEach(() => {
    vi.clearAllMocks();
    mockAuthorize = vi.fn().mockResolvedValue('mut-token');
    mockUnauthorize = vi.fn().mockResolvedValue(undefined);
    (useMusicKit as Mock).mockReturnValue({
      ready: true,
      error: null,
      authorize: mockAuthorize,
      unauthorize: mockUnauthorize,
    });
  });

  const connectedStatus = (overrides = {}) => ({
    provider: 'apple_music',
    connected: true,
    connectedAt: '2026-01-01T00:00:00Z',
    canRefresh: true,
    ...overrides,
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

  it('surfaces a MusicKit setup failure instead of a silently disabled button', async () => {
    (useMusicKit as Mock).mockReturnValue({
      ready: false,
      error: 'Failed to load MusicKit script',
      authorize: mockAuthorize,
    });
    (getConnectionStatus as Mock).mockResolvedValue(
      connectedStatus({ provider: 'apple_music', connected: false })
    );

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Not Connected')).toBeInTheDocument()
    );

    expect(
      screen.getByRole('button', { name: /connect apple music/i })
    ).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(
      /Apple Music is unavailable: Failed to load MusicKit script/
    );
  });

  it('does NOT prompt Apple reconnect just because canRefresh is false', async () => {
    // Apple has no refresh flow — canRefresh is always false. A canRefresh heuristic
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

  it('disconnecting Apple Music also drops the browser-side MusicKit grant', async () => {
    // Without unauthorize(), the next authorize() silently reissues a Music User Token
    // with no consent popup — "disconnected" in our DB but not in the browser.
    const farExpiry = new Date(Date.now() + 100 * 24 * 3600 * 1000).toISOString();
    (getConnectionStatus as Mock)
      .mockResolvedValueOnce(
        connectedStatus({
          provider: 'apple_music',
          connected: true,
          canRefresh: false,
          expiresAt: farExpiry,
        })
      )
      .mockResolvedValueOnce(
        connectedStatus({ provider: 'apple_music', connected: false })
      );
    (disconnectProvider as Mock).mockResolvedValue({ success: true });
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Connected')).toBeInTheDocument()
    );

    await user.click(screen.getByRole('button', { name: /disconnect/i }));

    await waitFor(() =>
      expect(screen.getByText('Apple Music Not Connected')).toBeInTheDocument()
    );
    expect(disconnectProvider).toHaveBeenCalledWith('apple_music');
    expect(mockUnauthorize).toHaveBeenCalledOnce();
  });

  it('surfaces a failed disconnect and stays connected', async () => {
    (getConnectionStatus as Mock).mockResolvedValue(connectedStatus());
    (disconnectProvider as Mock).mockRejectedValue(
      new Error('API Error: 500 Internal Server Error')
    );
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const user = userEvent.setup();

    render(<ProviderConnectionStatus provider="apple_music" />);
    await waitFor(() =>
      expect(screen.getByText('Apple Music Connected')).toBeInTheDocument()
    );

    await user.click(screen.getByRole('button', { name: /disconnect/i }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(screen.getByRole('alert')).toHaveTextContent(
      /failed to disconnect apple music/i
    );
    expect(screen.getByText('Apple Music Connected')).toBeInTheDocument();
    consoleSpy.mockRestore();
  });
});
