import { Job, MusicProvider } from '../services/api';

export const PROVIDER_LABELS: Record<MusicProvider, string> = {
  apple_music: 'Apple Music',
};

export const providerLabel = (provider?: string): string =>
  PROVIDER_LABELS[(provider as MusicProvider) ?? 'apple_music'] ??
  'Apple Music';

/**
 * Public web URL for a playlist, or null when the platform has none. Apple Music
 * LIBRARY playlists (p.xxx ids) have no public URL — only catalog playlists (pl.xxx) do.
 */
export const playlistUrl = (
  provider: MusicProvider | undefined,
  playlistId: string
): string | null => {
  return playlistId.startsWith('pl.')
    ? `https://music.apple.com/library/playlist/${playlistId}`
    : null;
};

/** Public web URL for a track, or null when the platform can't deep-link the id. */
export const trackUrl = (
  provider: MusicProvider | undefined,
  trackId: string
): string | null => {
  // Catalog song ids are numeric; library ids (i.xxx) have no public URL.
  return /^\d+$/.test(trackId)
    ? `https://music.apple.com/song/${trackId}`
    : null;
};

/** Human label for what a job does, e.g. "Clean", "Copy to Apple Music". */
export const jobTypeLabel = (job: Job): string => {
  if (job.jobType !== 'copy') return 'Clean';
  const target = providerLabel(job.targetProvider);
  return job.swapExplicitForClean ? `Clean copy to ${target}` : `Copy to ${target}`;
};
