import { createClient as createServerClient } from '@/lib/supabase/server';
import { createClient as createClientClient } from '@/lib/supabase/client';

// e.g., 'types/api.ts', and import them here.
/**
 * The music service RadioWash works with. A union of one today — Apple Music is
 * the only supported provider — but kept as a named type because the backend
 * abstraction is provider-neutral and a second service is expected.
 */
export type MusicProvider = 'apple_music';

export interface User {
  id: number;
  supabaseId: string;
  displayName: string;
  email: string;
  profileImageUrl?: string;
}
export interface Playlist {
  id: string;
  name: string;
  description?: string;
  imageUrl?: string;
  trackCount: number;
  ownerId: string;
  ownerName?: string;
}
export interface Job {
  id: number;
  provider: MusicProvider;
  targetProvider: MusicProvider;
  jobType: 'clean' | 'copy';
  swapExplicitForClean: boolean;
  sourcePlaylistId: string;
  sourcePlaylistName: string;
  targetPlaylistId?: string;
  targetPlaylistName?: string;
  status: string;
  errorMessage?: string;
  totalTracks: number;
  processedTracks: number;
  matchedTracks: number;
  currentBatch?: string;
  batchSize?: number;
  createdAt: string;
  updatedAt: string;
}
export interface TrackMapping {
  id: number;
  sourceTrackId: string;
  sourceTrackName: string;
  sourceArtistName: string;
  isExplicit: boolean;
  targetTrackId?: string;
  targetTrackName?: string;
  targetArtistName?: string;
  hasCleanMatch: boolean;
  isrc?: string;
  matchMethod?: string;
}

export interface SubscriptionStatus {
  hasActiveSubscription: boolean;
  subscriptionId: number | null;
  planName: string | null;
  status: string | null;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
}

export interface UserSubscriptionDto {
  id: number;
  status: string;
  currentPeriodStart?: string;
  currentPeriodEnd?: string;
  canceledAt?: string;
  cancelAtPeriodEnd?: boolean;
  plan: SubscriptionPlanDto;
  createdAt: string;
}

export interface PlaylistSyncConfig {
  id: number;
  originalJobId: number;
  sourcePlaylistId: string;
  sourcePlaylistName: string;
  targetPlaylistId: string;
  targetPlaylistName: string;
  isActive: boolean;
  syncFrequency: string;
  lastSyncedAt?: string;
  lastSyncStatus?: string;
  lastSyncError?: string;
  nextScheduledSync?: string;
  createdAt: string;
}

export interface SyncResult {
  success: boolean;
  tracksAdded: number;
  tracksRemoved: number;
  tracksUnchanged: number;
  errorMessage?: string;
  executionTimeMs: number;
}

export interface SyncHistory {
  id: number;
  startedAt: string;
  completedAt?: string;
  status: string;
  tracksAdded?: number;
  tracksRemoved?: number;
  tracksUnchanged?: number;
  errorMessage?: string;
  executionTimeMs?: number;
}

export const API_BASE_URL =
  (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';

// Typed API error carrying the HTTP status and, when the backend returned an
// RFC 7807 Problem Details body, its human-readable detail and problem type.
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly detail?: string,
    public readonly problemType?: string
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

// Raw (non-Problem-Details) error bodies can be huge HTML pages — keep only
// enough to be useful in a message.
const MAX_RAW_ERROR_BODY_LENGTH = 200;

const truncate = (text: string) =>
  text.length > MAX_RAW_ERROR_BODY_LENGTH
    ? `${text.slice(0, MAX_RAW_ERROR_BODY_LENGTH)}…`
    : text;

// Builds an ApiError from a non-ok response body. Problem Details bodies
// (title/detail/type) are unpacked; anything else falls back to the raw text.
const toApiError = (
  status: number,
  statusText: string,
  body: string
): ApiError => {
  try {
    const parsed = JSON.parse(body);
    if (parsed && typeof parsed === 'object') {
      // Only trust string fields — a numeric or object title/detail must
      // never become the error message.
      const title = typeof parsed.title === 'string' ? parsed.title : undefined;
      const detail =
        typeof parsed.detail === 'string' ? parsed.detail : undefined;
      const message = title ?? detail;
      if (message) {
        return new ApiError(
          status,
          message,
          detail,
          typeof parsed.type === 'string' ? parsed.type : undefined
        );
      }
    }
  } catch {
    // Not JSON — fall through to the raw-text fallback.
  }
  return new ApiError(
    status,
    truncate(body) || statusText || `Request failed with status ${status}`
  );
};

// Server-side API function
export const fetchWithSupabaseAuthServer = async (
  url: string,
  options: RequestInit = {}
) => {
  const supabase = await createServerClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();

  const token = session?.access_token;

  if (!token) {
    throw new Error('User not authenticated');
  }

  const response = await fetch(url, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...options.headers,
    },
  });

  if (!response.ok) {
    const errorBody = await response.text();
    console.error(
      `API Error: ${response.status} ${response.statusText}`,
      `Error Body: "${errorBody}"`,
      `URL: "${url}"`
    );
    throw toApiError(response.status, response.statusText, errorBody);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.indexOf('application/json') !== -1) {
    const json = await response.json();
    return json;
  }
  return;
};

// Client-side API function
export const fetchWithSupabaseAuth = async (
  url: string,
  options: RequestInit = {}
) => {
  const supabase = createClientClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();

  const token = session?.access_token;

  if (!token) {
    throw new Error('User not authenticated');
  }

  const response = await fetch(url, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...options.headers,
    },
  });

  if (!response.ok) {
    const errorBody = await response.text();
    console.error(
      `API Error: ${response.status} ${response.statusText}`,
      `Error Body: "${errorBody}"`,
      `URL: "${url}"`
    );
    throw toApiError(response.status, response.statusText, errorBody);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.indexOf('application/json') !== -1) {
    const json = await response.json();
    return json;
  }
  return;
};

// --- Server-side API Functions ---
export const getMeServer = async (): Promise<User> => {
  const result = await fetchWithSupabaseAuthServer(`${API_BASE_URL}/auth/me`);
  return result;
};

export const getUserPlaylistsServer = (
  provider: MusicProvider = 'apple_music'
): Promise<
  Playlist[] | { error: string; message: string; playlists: Playlist[] }
> =>
  fetchWithSupabaseAuthServer(
    `${API_BASE_URL}/playlist/user/me?provider=${provider}`
  );

export const getUserJobsServer = (): Promise<Job[]> =>
  fetchWithSupabaseAuthServer(`${API_BASE_URL}/cleanplaylist/user/me/jobs`);

export const getUserJobDetailsServer = (jobId: number): Promise<Job> => {
  return fetchWithSupabaseAuthServer(
    `${API_BASE_URL}/cleanplaylist/user/me/job/${jobId}`
  );
};

export const getJobDetailsServer = (
  userId: number,
  jobId: number
): Promise<Job> => {
  return fetchWithSupabaseAuthServer(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}`
  );
};
// --- Client-side API Functions ---
export const getMe = async (): Promise<User> => {
  const result = await fetchWithSupabaseAuth(`${API_BASE_URL}/auth/me`);
  return result;
};

export interface ConnectionStatus {
  provider: MusicProvider;
  connected: boolean;
  connectedAt?: string;
  lastRefreshAt?: string;
  canRefresh: boolean;
  expiresAt?: string;
}

export const getConnectionStatus = (
  provider: MusicProvider
): Promise<ConnectionStatus> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/auth/status/${provider}`);

export const storeProviderTokens = (
  provider: MusicProvider,
  accessToken: string,
  refreshToken?: string
): Promise<{ success: boolean }> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/auth/tokens/${provider}`, {
    method: 'POST',
    body: JSON.stringify({ accessToken, refreshToken: refreshToken ?? null }),
  });

// Deletes the tokens stored for the provider. The credential itself can only be revoked
// provider-side (Spotify account page / Apple settings) — the API has no way to do that.
export const disconnectProvider = (
  provider: MusicProvider
): Promise<{ success: boolean }> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/auth/tokens/${provider}`, {
    method: 'DELETE',
  });

export const getMusicKitDeveloperToken = (): Promise<{ token: string }> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/auth/musickit/devtoken`);

export const getUserPlaylists = (
  provider: MusicProvider = 'apple_music'
): Promise<
  Playlist[] | { error: string; message: string; playlists: Playlist[] }
> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/playlist/user/me?provider=${provider}`);

export const getJobTrackMappings = (
  userId: number,
  jobId: number
): Promise<TrackMapping[]> =>
  fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}/tracks`
  );

export const getUserJobs = (): Promise<Job[]> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/cleanplaylist/user/me/jobs`);

export interface CreateJobOptions {
  sourcePlaylistId: string;
  targetPlaylistName?: string;
  provider?: MusicProvider;
  targetProvider?: MusicProvider;
  swapExplicitForClean?: boolean;
}

export const createCleanPlaylistJob = (
  userId: number,
  options: CreateJobOptions
): Promise<Job> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job`,
    {
      method: 'POST',
      body: JSON.stringify(options),
    }
  );
};

export const getJobDetails = (userId: number, jobId: number): Promise<Job> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}`
  );
};

// --- Subscription API Functions ---
export const getSubscriptionStatus = (): Promise<SubscriptionStatus> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/status`);
};

export const getCurrentSubscription = (): Promise<UserSubscriptionDto | null> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/current`);
};

export interface SubscriptionPlanDto {
  id: number;
  name: string;
  price: number;
  billingPeriod: string;
  stripePriceId?: string;
  maxPlaylists?: number;
  maxTracksPerPlaylist?: number;
  features: string[];
  isActive: boolean;
}

export const getAvailablePlans = (): Promise<SubscriptionPlanDto[]> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/plans`);
};

export const subscribeToSync = (): Promise<{ checkoutUrl?: string }> => {
  // planId null lets the backend pick the default plan; the Stripe price is
  // resolved server-side. clientRequestId makes the checkout idempotent.
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/checkout`, {
    method: 'POST',
    body: JSON.stringify({ planId: null, clientRequestId: crypto.randomUUID() }),
  });
};

// Reconciles a completed Stripe Checkout session with the local subscription
// state. Idempotent — safe to call repeatedly.
export const completeCheckout = (
  sessionId: string
): Promise<SubscriptionStatus> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/subscription/checkout/complete`,
    {
      method: 'POST',
      body: JSON.stringify({ sessionId }),
    }
  );
};

export const createPortalSession = (): Promise<{ portalUrl?: string }> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/portal`, {
    method: 'POST',
  });
};

export const cancelSubscription = (): Promise<{
  message: string;
  activeUntil?: string;
  cancelAtPeriodEnd?: boolean;
}> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/subscription/cancel`, {
    method: 'POST',
  });
};

// --- Sync Management API Functions ---
export const enableSyncForJob = (jobId: number): Promise<PlaylistSyncConfig> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/playlistsync/enable`, {
    method: 'POST',
    body: JSON.stringify({ jobId }),
  });
};

export const disableSync = (syncConfigId: number): Promise<{ success: boolean }> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/playlistsync/${syncConfigId}`, {
    method: 'DELETE',
  });
};

export const getSyncConfigs = (): Promise<PlaylistSyncConfig[]> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/playlistsync`);
};

export const updateSyncFrequency = (
  syncConfigId: number,
  frequency: string
): Promise<PlaylistSyncConfig> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/playlistsync/${syncConfigId}/frequency`, {
    method: 'PATCH',
    body: JSON.stringify({ frequency }),
  });
};

export const triggerManualSync = (syncConfigId: number): Promise<SyncResult> => {
  return fetchWithSupabaseAuth(`${API_BASE_URL}/playlistsync/${syncConfigId}/sync`, {
    method: 'POST',
  });
};

export const getSyncHistory = (
  syncConfigId: number,
  limit = 20
): Promise<SyncHistory[]> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/playlistsync/${syncConfigId}/history?limit=${limit}`
  );
};
