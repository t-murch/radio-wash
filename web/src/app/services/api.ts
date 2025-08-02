// It's good practice to define your types in a separate file,

import { createClient as createServerClient } from '@/lib/supabase/server';
import { createClient as createClientClient } from '@/lib/supabase/client';

// e.g., 'types/api.ts', and import them here.
export interface User {
  id: number;
  spotifyId: string;
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
}

export const API_BASE_URL =
  (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';

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
    throw new Error(`Request failed: ${response.statusText}`);
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
    throw new Error(`Request failed: ${response.statusText}`);
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

export const getUserPlaylistsServer = (): Promise<
  Playlist[] | { error: string; message: string; playlists: Playlist[] }
> => fetchWithSupabaseAuthServer(`${API_BASE_URL}/playlist/user/me`);

export const getUserJobsServer = (): Promise<Job[]> =>
  fetchWithSupabaseAuthServer(`${API_BASE_URL}/cleanplaylist/user/me/jobs`);

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

export const getSpotifyConnectionStatus = async (): Promise<{
  connected: boolean;
  connectedAt?: string;
  lastRefreshAt?: string;
  canRefresh: boolean;
}> => {
  const result = await fetchWithSupabaseAuth(
    `${API_BASE_URL}/auth/spotify/status`
  );
  return result;
};

export const getUserPlaylists = (): Promise<
  Playlist[] | { error: string; message: string; playlists: Playlist[] }
> => fetchWithSupabaseAuth(`${API_BASE_URL}/playlist/user/me`);

export const getJobTrackMappings = (
  userId: number,
  jobId: number
): Promise<TrackMapping[]> =>
  fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}/tracks`
  );

export const getUserJobs = (): Promise<Job[]> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/cleanplaylist/user/me/jobs`);

export const createCleanPlaylistJob = (
  userId: number,
  sourcePlaylistId: string,
  targetPlaylistName?: string
): Promise<Job> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job`,
    {
      method: 'POST',
      body: JSON.stringify({ sourcePlaylistId, targetPlaylistName }),
    }
  );
};

export const getJobDetails = (userId: number, jobId: number): Promise<Job> => {
  return fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}`
  );
};
