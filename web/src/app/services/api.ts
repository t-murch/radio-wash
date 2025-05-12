const API_BASE_URL = 'http://localhost:5159/api';

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

export interface Track {
  id: string;
  name: string;
  artist: string;
  album: string;
  albumCover?: string;
  isExplicit: boolean;
  uri: string;
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

// Auth functions
export const getLoginUrl = async (): Promise<string> => {
  const response = await fetch(`${API_BASE_URL}/auth/login`);
  if (!response.ok) {
    throw new Error('Failed to get login URL');
  }
  const data = await response.json();
  return data.url;
};

export const handleCallback = async (
  code: string,
  state: string
): Promise<{ token: string; user: User }> => {
  const response = await fetch(
    `${API_BASE_URL}/auth/callback?code=${code}&state=${state}`,
    { credentials: 'include' }
  );
  if (!response.ok) {
    throw new Error('Authentication failed');
  }
  return await response.json();
};

export const validateToken = async (userId: number): Promise<boolean> => {
  try {
    const response = await fetch(
      `${API_BASE_URL}/auth/validate?userId=${userId}`
    );
    if (!response.ok) {
      return false;
    }
    const data = await response.json();
    return data.valid;
  } catch (error) {
    return false;
  }
};

// Playlist functions
export const getUserPlaylists = async (userId: number): Promise<Playlist[]> => {
  const response = await fetch(`${API_BASE_URL}/playlist/user/${userId}`);
  if (!response.ok) {
    throw new Error('Failed to get playlists');
  }
  return await response.json();
};

export const getPlaylistTracks = async (
  userId: number,
  playlistId: string
): Promise<Track[]> => {
  const response = await fetch(
    `${API_BASE_URL}/playlist/user/${userId}/playlist/${playlistId}/tracks`
  );
  if (!response.ok) {
    throw new Error('Failed to get playlist tracks');
  }
  return await response.json();
};

// Clean playlist functions
export const createCleanPlaylistJob = async (
  userId: number,
  sourcePlaylistId: string,
  targetPlaylistName?: string
): Promise<Job> => {
  const response = await fetch(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        sourcePlaylistId,
        targetPlaylistName,
      }),
    }
  );

  if (!response.ok) {
    throw new Error('Failed to create clean playlist job');
  }

  return await response.json();
};

export const getUserJobs = async (userId: number): Promise<Job[]> => {
  const response = await fetch(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/jobs`
  );
  if (!response.ok) {
    throw new Error('Failed to get jobs');
  }
  return await response.json();
};

export const getJob = async (userId: number, jobId: number): Promise<Job> => {
  const response = await fetch(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}`
  );
  if (!response.ok) {
    throw new Error('Failed to get job');
  }
  return await response.json();
};

export const getJobTrackMappings = async (
  userId: number,
  jobId: number
): Promise<TrackMapping[]> => {
  const response = await fetch(
    `${API_BASE_URL}/cleanplaylist/user/${userId}/job/${jobId}/tracks`
  );
  if (!response.ok) {
    throw new Error('Failed to get track mappings');
  }
  return await response.json();
};
