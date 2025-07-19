// It's good practice to define your types in a separate file,

import { createClient } from '@/lib/supabase/server';

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

const fetchWithSupabaseAuth = async (
  url: string,
  options: RequestInit = {}
) => {
  const supabase = await createClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();

  const token = session?.access_token;

  if (!token) {
    // This will be caught by React Query's error handling
    throw new Error('User not authenticated');
  }

  // console.log(`API Request: ${url}`);

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
      `Error Body: "${errorBody}"`
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

// --- API Functions ---
export const getMe = async (): Promise<User> => {
  const result = await fetchWithSupabaseAuth(`${API_BASE_URL}/auth/me`);
  console.log(`getMe result: ${JSON.stringify(result)}`);
  return result;
};

export const getUserPlaylists = (userId: number): Promise<Playlist[]> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/playlist/user/${userId}`);

export const getJobTrackMappings = (
  userId: number,
  jobId: number
): Promise<TrackMapping[]> =>
  fetchWithSupabaseAuth(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}/tracks`
  );

export const getUserJobs = (userId: number): Promise<Job[]> =>
  fetchWithSupabaseAuth(`${API_BASE_URL}/cleanplaylist/user/${userId}/jobs`);

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
