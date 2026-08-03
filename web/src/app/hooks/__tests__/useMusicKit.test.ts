import { describe, it, expect, vi, beforeEach, afterEach, Mock } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useMusicKit } from '../useMusicKit';
import { getMusicKitDeveloperToken } from '../../services/api';

vi.mock('../../services/api', () => ({
  getMusicKitDeveloperToken: vi.fn(),
}));

describe('useMusicKit', () => {
  let mockAuthorize: Mock;
  let mockConfigure: Mock;

  beforeEach(() => {
    mockAuthorize = vi.fn().mockResolvedValue('music-user-token-123');
    mockConfigure = vi.fn().mockResolvedValue({
      authorize: mockAuthorize,
      unauthorize: vi.fn(),
      isAuthorized: false,
    });

    // Pretend the script is already present so no network fetch happens.
    window.MusicKit = {
      configure: mockConfigure,
      getInstance: vi.fn(),
    };

    (getMusicKitDeveloperToken as Mock).mockResolvedValue({ token: 'dev-jwt' });
  });

  afterEach(() => {
    delete window.MusicKit;
    document.getElementById('musickit-js')?.remove();
    vi.restoreAllMocks();
  });

  it('configures MusicKit with the developer token from the API', async () => {
    const { result } = renderHook(() => useMusicKit());

    await waitFor(() => expect(result.current.ready).toBe(true));

    expect(getMusicKitDeveloperToken).toHaveBeenCalledOnce();
    expect(mockConfigure).toHaveBeenCalledWith({
      developerToken: 'dev-jwt',
      app: { name: 'RadioWash', build: '1.0.0' },
    });
    expect(result.current.error).toBeNull();
  });

  it('authorize resolves the Music User Token from the MusicKit instance', async () => {
    const { result } = renderHook(() => useMusicKit());
    await waitFor(() => expect(result.current.ready).toBe(true));

    const token = await result.current.authorize();

    expect(token).toBe('music-user-token-123');
    expect(mockAuthorize).toHaveBeenCalledOnce();
  });

  it('authorize throws while MusicKit is not ready', async () => {
    // Keep the hook stuck in setup by never resolving the dev-token request.
    (getMusicKitDeveloperToken as Mock).mockReturnValue(new Promise(() => undefined));

    const { result } = renderHook(() => useMusicKit());

    await expect(result.current.authorize()).rejects.toThrow('MusicKit is not ready');
  });

  it('surfaces an error when the developer token request fails (Apple not configured)', async () => {
    (getMusicKitDeveloperToken as Mock).mockRejectedValue(
      new Error('Request failed: Service Unavailable')
    );
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const { result } = renderHook(() => useMusicKit());

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.ready).toBe(false);

    consoleSpy.mockRestore();
  });

  it('does no work when disabled (non-Apple providers)', async () => {
    delete window.MusicKit;

    const { result } = renderHook(() => useMusicKit({ enabled: false }));

    // Nothing to wait on — assert the absence of the setup side effects.
    await waitFor(() =>
      expect(getMusicKitDeveloperToken).not.toHaveBeenCalled()
    );
    expect(document.getElementById('musickit-js')).toBeNull();
    expect(result.current.ready).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('injects the MusicKit script when the global is absent', async () => {
    delete window.MusicKit;

    renderHook(() => useMusicKit());

    await waitFor(() =>
      expect(document.getElementById('musickit-js')).not.toBeNull()
    );
    expect(
      (document.getElementById('musickit-js') as HTMLScriptElement).src
    ).toContain('js-cdn.music.apple.com/musickit/v3/musickit.js');
  });

  it('removes the script tag on load failure so a later mount can retry', async () => {
    // A dead tag left in the DOM would make every subsequent mount attach to it and wait
    // forever on a `musickitloaded` event that already failed to fire.
    delete window.MusicKit;
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const { result } = renderHook(() => useMusicKit());
    await waitFor(() =>
      expect(document.getElementById('musickit-js')).not.toBeNull()
    );

    document.getElementById('musickit-js')!.dispatchEvent(new Event('error'));

    await waitFor(() =>
      expect(result.current.error).toBe('Failed to load MusicKit script')
    );
    expect(document.getElementById('musickit-js')).toBeNull();

    consoleSpy.mockRestore();
  });
});
