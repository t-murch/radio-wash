export const API_BASE_URL =
  (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';

// --- Interfaces (assuming they are defined as before) ---
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

// --- API Functions (Refactored for Cookie Auth) ---

const fetchWithCredentials = async (url: string, options: RequestInit = {}) => {
  const response = await fetch(url, {
    ...options,
    credentials: 'include', // Automatically sends cookies
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });

  if (!response.ok) {
    const errorBody = await response.text();
    console.error(
      `API Error: ${response.status} ${response.statusText}`,
      errorBody
    );
    throw new Error(`Request failed: ${response.statusText}`);
  }

  // Handle cases where response might be empty (e.g., 204 No Content)
  const contentType = response.headers.get('content-type');
  if (contentType && contentType.indexOf('application/json') !== -1) {
    return response.json();
  }
  return;
};

// --- Auth Functions ---
export const getMe = (): Promise<User> =>
  fetchWithCredentials(`${API_BASE_URL}/auth/me`);

export const logout = (): Promise<void> =>
  fetchWithCredentials(`${API_BASE_URL}/auth/logout`, { method: 'POST' });

// --- Playlist Functions ---
export const getUserPlaylists = (userId: number): Promise<Playlist[]> =>
  fetchWithCredentials(`${API_BASE_URL}/playlist/user/${userId}`);

export const getJobTrackMappings = (
  userId: number,
  jobId: number
): Promise<TrackMapping> =>
  fetchWithCredentials(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}/tracks`
  );

// --- Job Functions ---
export const getUserJobs = (userId: number): Promise<Job[]> =>
  fetchWithCredentials(`${API_BASE_URL}/cleanplaylist/user/${userId}/jobs`);

export const createCleanPlaylistJob = (
  userId: number,
  sourcePlaylistId: string,
  targetPlaylistName?: string
): Promise<Job> => {
  return fetchWithCredentials(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job`,
    {
      method: 'POST',
      body: JSON.stringify({ sourcePlaylistId, targetPlaylistName }),
    }
  );
};

export const getJobDetails = async (
  userId: number,
  jobId: number
): Promise<Job> => {
  const API_BASE_URL =
    (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';

  const response = await fetch(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}`,
    {
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
    }
  );

  if (!response.ok) {
    throw new Error(`Failed to get job: ${response.statusText}`);
  }

  return response.json();
};
