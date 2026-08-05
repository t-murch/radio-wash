/**
 * Fixture data for design screenshot capture.
 *
 * Shapes are derived from the real contracts, not invented:
 *   - web/src/app/services/api.ts        (User, Playlist, Job, TrackMapping, ...)
 *   - api/Models/DTO/                    (CleanPlaylistJobDto, SubscriptionDto, ...)
 *   - api/Models/Domain/CleanPlaylistJob.cs  (JobStatus / JobTypes constants)
 *
 * Every timestamp is a fixed literal. Relative dates ("2 hours ago") would make
 * each capture run produce different pixels and defeat visual diffing.
 */

// Anchor for anything the UI renders as a date. Chosen to sit inside the Apple
// reconnect window for the "needs reconnect" scenario below.
export const NOW = '2026-08-04T12:00:00.000Z';

const user = {
  id: 1,
  spotifyId: 'radiowash_demo',
  displayName: 'Alex Rivera',
  email: 'alex@example.com',
  profileImageUrl: undefined,
};

/**
 * Deliberately awkward real-world data: a very long playlist name, a
 * single-track playlist, a 0-track playlist, and an empty description.
 * Neat 3-word names hide the layout bugs a redesign needs to catch.
 */
const spotifyPlaylists = [
  {
    id: '37i9dQZF1DXcBWIGoYBM5M',
    name: 'Today’s Top Hits',
    description: 'The hottest tracks right now.',
    imageUrl: undefined,
    trackCount: 50,
    ownerId: 'spotify',
    ownerName: 'Spotify',
  },
  {
    id: '2fmTTbBkXi8pewbUvG3CeZ',
    name: 'Late Night Drive — Extended Mix for Long Commutes and Rainy Evenings',
    description: '',
    imageUrl: undefined,
    trackCount: 187,
    ownerId: 'radiowash_demo',
    ownerName: 'Alex Rivera',
  },
  {
    id: '5Rrf7mqNjyhFyGw2HcAyZq',
    name: 'Gym',
    description: 'Heavy rotation.',
    imageUrl: undefined,
    trackCount: 1,
    ownerId: 'radiowash_demo',
    ownerName: 'Alex Rivera',
  },
  {
    id: '1BdwmpNCFuNyeJcCqOJZDm',
    name: 'Kids Road Trip',
    description: 'Needs to be clean.',
    imageUrl: undefined,
    trackCount: 64,
    ownerId: 'radiowash_demo',
    ownerName: 'Alex Rivera',
  },
];

/**
 * Apple library playlists report trackCount: 0 — AppleMusicMusicService omits
 * the attribute entirely. This is a real constraint the redesign must absorb,
 * so the fixture reproduces it rather than papering over it.
 */
const applePlaylists = [
  {
    id: 'p.LV0PXNoCl9rkzVA',
    name: 'Chill Mix',
    description: '',
    imageUrl: undefined,
    trackCount: 0,
    ownerId: 'apple_demo',
    ownerName: 'Alex Rivera',
  },
  {
    id: 'p.QvDzXkLuMNrkzVB',
    name: 'Workout',
    description: '',
    imageUrl: undefined,
    trackCount: 0,
    ownerId: 'apple_demo',
    ownerName: 'Alex Rivera',
  },
];

const jobCompleted = {
  id: 101,
  provider: 'spotify',
  targetProvider: 'spotify',
  jobType: 'clean',
  swapExplicitForClean: true,
  sourcePlaylistId: '1BdwmpNCFuNyeJcCqOJZDm',
  sourcePlaylistName: 'Kids Road Trip',
  targetPlaylistId: '7substituteTargetId01',
  targetPlaylistName: 'Clean - Kids Road Trip',
  status: 'Completed',
  errorMessage: undefined,
  totalTracks: 64,
  processedTracks: 64,
  matchedTracks: 61,
  currentBatch: 'Completed',
  batchSize: 100,
  createdAt: '2026-08-04T09:12:00.000Z',
  updatedAt: '2026-08-04T09:14:37.000Z',
};

const jobProcessing = {
  id: 102,
  provider: 'spotify',
  targetProvider: 'apple_music',
  jobType: 'copy',
  swapExplicitForClean: true,
  sourcePlaylistId: '2fmTTbBkXi8pewbUvG3CeZ',
  sourcePlaylistName:
    'Late Night Drive — Extended Mix for Long Commutes and Rainy Evenings',
  targetPlaylistId: undefined,
  targetPlaylistName: 'Clean - Late Night Drive',
  status: 'Processing',
  errorMessage: undefined,
  totalTracks: 187,
  processedTracks: 88,
  matchedTracks: 79,
  currentBatch: 'Batch 1 of 2',
  batchSize: 100,
  createdAt: '2026-08-04T11:58:00.000Z',
  updatedAt: '2026-08-04T11:59:20.000Z',
};

const jobFailed = {
  id: 103,
  provider: 'apple_music',
  targetProvider: 'apple_music',
  jobType: 'clean',
  swapExplicitForClean: true,
  sourcePlaylistId: 'p.LV0PXNoCl9rkzVA',
  sourcePlaylistName: 'Chill Mix',
  targetPlaylistId: undefined,
  targetPlaylistName: undefined,
  status: 'Failed',
  errorMessage:
    'Apple Music rejected the request: the stored Music User Token has expired. Reconnect Apple Music and try again.',
  totalTracks: 42,
  processedTracks: 12,
  matchedTracks: 8,
  currentBatch: undefined,
  batchSize: 100,
  createdAt: '2026-08-03T18:40:00.000Z',
  updatedAt: '2026-08-03T18:41:05.000Z',
};

const jobPending = {
  id: 104,
  provider: 'spotify',
  targetProvider: 'spotify',
  jobType: 'clean',
  swapExplicitForClean: true,
  sourcePlaylistId: '5Rrf7mqNjyhFyGw2HcAyZq',
  sourcePlaylistName: 'Gym',
  targetPlaylistId: undefined,
  targetPlaylistName: 'Clean - Gym',
  status: 'Pending',
  errorMessage: undefined,
  totalTracks: 1,
  processedTracks: 0,
  matchedTracks: 0,
  currentBatch: undefined,
  batchSize: 100,
  createdAt: '2026-08-04T11:59:50.000Z',
  updatedAt: '2026-08-04T11:59:50.000Z',
};

/**
 * All four MatchMethod values that MatchMethodChip renders, plus an unmatched
 * row and a clean-original row. Covers every visual branch of the table.
 */
const trackMappings = [
  {
    id: 1,
    sourceTrackId: '3n3Ppam7vgaVa1iaRUc9Lp',
    sourceTrackName: 'Mr. Brightside',
    sourceArtistName: 'The Killers',
    isExplicit: false,
    targetTrackId: '3n3Ppam7vgaVa1iaRUc9Lp',
    targetTrackName: 'Mr. Brightside',
    targetArtistName: 'The Killers',
    hasCleanMatch: true,
    isrc: 'USIR10211356',
    matchMethod: 'isrc',
  },
  {
    id: 2,
    sourceTrackId: '7lQ8MOhq6IN2w8EYcFNSUk',
    sourceTrackName: 'Without Me',
    sourceArtistName: 'Eminem',
    isExplicit: true,
    targetTrackId: '2pQXKKfnMOZlHIvhL5vQxZ',
    targetTrackName: 'Without Me (Clean)',
    targetArtistName: 'Eminem',
    hasCleanMatch: true,
    isrc: 'USIR10211357',
    matchMethod: 'isrc-clean',
  },
  {
    id: 3,
    sourceTrackId: '5W3cjX2J3tjhG8zb6u0qHn',
    sourceTrackName: 'HUMBLE.',
    sourceArtistName: 'Kendrick Lamar',
    isExplicit: true,
    targetTrackId: '8Xq2LmPqR4tvJ9zc7v1rIo',
    targetTrackName: 'HUMBLE. (Radio Edit)',
    targetArtistName: 'Kendrick Lamar',
    hasCleanMatch: true,
    isrc: undefined,
    matchMethod: 'search-clean',
  },
  {
    id: 4,
    sourceTrackId: '1Qrg8KqiBpW07V7PNxwwwL',
    sourceTrackName: 'Kiss Me More',
    sourceArtistName: 'Doja Cat, SZA',
    isExplicit: false,
    targetTrackId: '4Lm9KqiBpW07V7PNxwwwQz',
    targetTrackName: 'Kiss Me More',
    targetArtistName: 'Doja Cat, SZA',
    hasCleanMatch: true,
    isrc: undefined,
    matchMethod: 'search',
  },
  {
    id: 5,
    sourceTrackId: '0e7ipj03S05BNilyu5bRzt',
    sourceTrackName: 'rockstar',
    sourceArtistName: 'Post Malone, 21 Savage',
    isExplicit: true,
    targetTrackId: undefined,
    targetTrackName: undefined,
    targetArtistName: undefined,
    hasCleanMatch: false,
    isrc: 'USUM71713948',
    matchMethod: 'none',
  },
];

const planSync = {
  id: 1,
  name: 'Sync Plan',
  priceInCents: 500,
  billingPeriod: 'monthly',
  stripePriceId: 'price_demo_monthly',
  maxPlaylists: 10,
  maxTracksPerPlaylist: 200,
  features: [
    'Daily automatic sync',
    'Up to 10 sync configurations',
    'Up to 200 tracks per playlist',
    'Manual sync triggering',
    'Sync history and status',
    'Smart track matching',
  ],
  isActive: true,
};

const syncConfigs = [
  {
    id: 11,
    originalJobId: 101,
    sourcePlaylistId: '1BdwmpNCFuNyeJcCqOJZDm',
    sourcePlaylistName: 'Kids Road Trip',
    targetPlaylistId: '7substituteTargetId01',
    targetPlaylistName: 'Clean - Kids Road Trip',
    isActive: true,
    syncFrequency: 'daily',
    lastSyncedAt: '2026-08-04T00:01:12.000Z',
    lastSyncStatus: 'completed',
    lastSyncError: undefined,
    nextScheduledSync: '2026-08-05T00:01:00.000Z',
    createdAt: '2026-07-20T14:02:00.000Z',
  },
  {
    id: 12,
    originalJobId: 105,
    sourcePlaylistId: '37i9dQZF1DXcBWIGoYBM5M',
    sourcePlaylistName: 'Today’s Top Hits',
    targetPlaylistId: '9substituteTargetId02',
    targetPlaylistName: 'Clean - Today’s Top Hits',
    isActive: false,
    syncFrequency: 'weekly',
    lastSyncedAt: '2026-08-01T00:01:44.000Z',
    lastSyncStatus: 'failed',
    lastSyncError: 'Spotify returned 429 (rate limited). Retrying next cycle.',
    nextScheduledSync: '2026-08-08T00:01:00.000Z',
    createdAt: '2026-07-11T08:30:00.000Z',
  },
];

const syncHistory = [
  {
    id: 501,
    startedAt: '2026-08-04T00:01:00.000Z',
    completedAt: '2026-08-04T00:01:12.000Z',
    status: 'completed',
    tracksAdded: 3,
    tracksRemoved: 1,
    tracksUnchanged: 60,
    errorMessage: undefined,
    executionTimeMs: 12408,
  },
  {
    id: 500,
    startedAt: '2026-08-03T00:01:00.000Z',
    completedAt: '2026-08-03T00:01:09.000Z',
    status: 'completed',
    tracksAdded: 0,
    tracksRemoved: 0,
    tracksUnchanged: 61,
    errorMessage: undefined,
    executionTimeMs: 9120,
  },
  {
    id: 499,
    startedAt: '2026-08-02T00:01:00.000Z',
    completedAt: '2026-08-02T00:01:03.000Z',
    status: 'failed',
    tracksAdded: 0,
    tracksRemoved: 0,
    tracksUnchanged: 0,
    errorMessage: 'Spotify returned 429 (rate limited).',
    executionTimeMs: 3011,
  },
];

const connected = (overrides = {}) => ({
  connected: true,
  canRefresh: true,
  connectedAt: '2026-07-02T16:20:00.000Z',
  lastRefreshAt: '2026-08-04T06:00:00.000Z',
  expiresAt: '2026-11-02T16:20:00.000Z',
  ...overrides,
});

const disconnected = {
  connected: false,
  canRefresh: false,
  connectedAt: undefined,
  lastRefreshAt: undefined,
  expiresAt: undefined,
};

const subInactive = {
  hasActiveSubscription: false,
  subscriptionId: null,
  planName: null,
  status: null,
  currentPeriodEnd: null,
  cancelAtPeriodEnd: false,
};

const subActive = {
  hasActiveSubscription: true,
  subscriptionId: 9001,
  planName: 'Sync Plan',
  status: 'active',
  currentPeriodEnd: '2026-09-04T12:00:00.000Z',
  cancelAtPeriodEnd: false,
};

/**
 * A scenario is the complete API surface for one screenshot. The mock server
 * reads whichever scenario the current request names via the x-rw-scenario
 * header (browser) or the RW_SCENARIO default (server-side render).
 */
export const scenarios = {
  /** Brand-new user: signed in, nothing connected, no playlists, no jobs. */
  'dashboard-empty': {
    user,
    playlists: [],
    jobs: [],
    connections: { spotify: disconnected, apple_music: disconnected },
    subscription: subInactive,
    syncConfigs: [],
  },

  /** The common case today: Spotify connected, Apple not. */
  'dashboard-spotify-only': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobCompleted, jobPending],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subInactive,
    syncConfigs: [],
  },

  /** The target state for the redesign: both providers live. */
  'dashboard-both-connected': {
    user,
    playlists: [...spotifyPlaylists, ...applePlaylists],
    jobs: [jobProcessing, jobCompleted, jobFailed, jobPending],
    connections: {
      spotify: connected(),
      apple_music: connected({ canRefresh: false }),
    },
    subscription: subActive,
    syncConfigs,
  },

  /**
   * Apple token inside the 14-day reconnect window (APPLE_RECONNECT_WINDOW_DAYS
   * in ProviderConnectionStatus.tsx). Surfaces the Reconnect button, which has
   * no Spotify equivalent.
   */
  'dashboard-apple-reconnect': {
    user,
    playlists: [...spotifyPlaylists, ...applePlaylists],
    jobs: [jobFailed],
    connections: {
      spotify: connected(),
      apple_music: connected({
        canRefresh: false,
        expiresAt: '2026-08-10T12:00:00.000Z',
      }),
    },
    subscription: subActive,
    syncConfigs,
  },

  /** Apple-only: the world the redesign is meant to center. */
  'dashboard-apple-only': {
    user,
    playlists: applePlaylists,
    jobs: [jobFailed],
    connections: {
      spotify: disconnected,
      apple_music: connected({ canRefresh: false }),
    },
    subscription: subInactive,
    syncConfigs: [],
  },

  'job-completed': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobCompleted],
    job: jobCompleted,
    trackMappings,
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subActive,
    syncConfigs,
  },

  'job-processing': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobProcessing],
    job: jobProcessing,
    trackMappings: trackMappings.slice(0, 3),
    connections: {
      spotify: connected(),
      apple_music: connected({ canRefresh: false }),
    },
    subscription: subActive,
    syncConfigs,
  },

  'job-failed': {
    user,
    playlists: applePlaylists,
    jobs: [jobFailed],
    job: jobFailed,
    trackMappings: trackMappings.slice(0, 2),
    connections: {
      spotify: disconnected,
      apple_music: connected({ canRefresh: false }),
    },
    subscription: subInactive,
    syncConfigs: [],
  },

  'job-pending': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobPending],
    job: jobPending,
    trackMappings: [],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subInactive,
    syncConfigs: [],
  },

  /** Free user hitting the paid-feature gate. */
  'sync-free': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobCompleted],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subInactive,
    syncConfigs: [],
    syncHistory: [],
  },

  /** Paying user with one healthy config and one auto-disabled failure. */
  'sync-pro': {
    user,
    playlists: spotifyPlaylists,
    jobs: [jobCompleted],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subActive,
    syncConfigs,
    syncHistory,
  },

  'subscription-free': {
    user,
    playlists: spotifyPlaylists,
    jobs: [],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subInactive,
    plans: [planSync],
    syncConfigs: [],
  },

  'subscription-active': {
    user,
    playlists: spotifyPlaylists,
    jobs: [],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: subActive,
    plans: [planSync],
    syncConfigs,
  },

  /** Cancel-at-period-end: distinct copy from a plain active subscription. */
  'subscription-canceling': {
    user,
    playlists: spotifyPlaylists,
    jobs: [],
    connections: { spotify: connected(), apple_music: disconnected },
    subscription: { ...subActive, cancelAtPeriodEnd: true },
    plans: [planSync],
    syncConfigs,
  },
};

export const DEFAULT_SCENARIO = 'dashboard-both-connected';
