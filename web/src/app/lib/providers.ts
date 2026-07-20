import { Job, MusicProvider } from '../services/api';

export const PROVIDER_LABELS: Record<MusicProvider, string> = {
  spotify: 'Spotify',
  apple_music: 'Apple Music',
};

export const providerLabel = (provider?: string): string =>
  PROVIDER_LABELS[(provider as MusicProvider) ?? 'spotify'] ?? 'Spotify';

/**
 * Public web URL for a playlist, or null when the platform has none. Apple Music
 * LIBRARY playlists (p.xxx ids) have no public URL — only catalog playlists (pl.xxx) do.
 */
export const playlistUrl = (
  provider: MusicProvider | undefined,
  playlistId: string
): string | null => {
  if (provider === 'apple_music') {
    return playlistId.startsWith('pl.')
      ? `https://music.apple.com/library/playlist/${playlistId}`
      : null;
  }
  return `https://open.spotify.com/playlist/${playlistId}`;
};

/** Public web URL for a track, or null when the platform can't deep-link the id. */
export const trackUrl = (
  provider: MusicProvider | undefined,
  trackId: string
): string | null => {
  if (provider === 'apple_music') {
    // Catalog song ids are numeric; library ids (i.xxx) have no public URL.
    return /^\d+$/.test(trackId)
      ? `https://music.apple.com/song/${trackId}`
      : null;
  }
  return `https://open.spotify.com/track/${trackId}`;
};

/** Human label for what a job does, e.g. "Clean", "Copy to Apple Music". */
export const jobTypeLabel = (job: Job): string => {
  if (job.jobType !== 'copy') return 'Clean';
  const target = providerLabel(job.targetProvider);
  return job.swapExplicitForClean ? `Clean copy to ${target}` : `Copy to ${target}`;
};
