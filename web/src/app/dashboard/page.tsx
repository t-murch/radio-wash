'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  createCleanPlaylistJob,
  getUserJobs,
  getUserPlaylists,
  validateToken,
} from '@/services/api';

interface User {
  id: number;
  spotifyId: string;
  displayName: string;
  email: string;
  profileImageUrl?: string;
}

interface Playlist {
  id: string;
  name: string;
  description?: string;
  imageUrl?: string;
  trackCount: number;
  ownerId: string;
  ownerName?: string;
}

interface Job {
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

export default function DashboardPage() {
  const router = useRouter();
  const [user, setUser] = useState<User | null>(null);
  const [playlists, setPlaylists] = useState<Playlist[]>([]);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedPlaylist, setSelectedPlaylist] = useState<Playlist | null>(
    null
  );
  const [customName, setCustomName] = useState('');
  const [processingPlaylist, setProcessingPlaylist] = useState(false);

  useEffect(() => {
    // Check if user is logged in
    const storedUser = localStorage.getItem('radiowash_user');
    const storedToken = localStorage.getItem('radiowash_token');

    if (!storedUser || !storedToken) {
      router.push('/auth');
      return;
    }

    const parsedUser = JSON.parse(storedUser) as User;
    setUser(parsedUser);

    // validateTokenAsync(parsedUser.id);

    // Load user data
    loadUserData(parsedUser.id);
  }, [router]);

  // const validateTokenAsync = async (userId: number) => {
  //   return await validateToken(userId);
  // };
  const loadUserData = async (userId: number) => {
    try {
      setLoading(true);
      setError(null);

      await validateToken(userId);

      // Load playlists
      const playlistsData = await getUserPlaylists(userId);
      setPlaylists(playlistsData);

      // Load jobs
      const jobsData = await getUserJobs(userId);
      setJobs(jobsData);
    } catch (error) {
      console.error('Error loading data:', error);
      setError('Failed to load your data. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleCreateCleanPlaylist = async () => {
    if (!selectedPlaylist || !user) return;

    try {
      setProcessingPlaylist(true);
      setError(null);

      const targetName =
        customName.trim() || `Clean - ${selectedPlaylist.name}`;

      const jobData = await createCleanPlaylistJob(
        user.id,
        selectedPlaylist.id,
        targetName
      );
      setJobs((prevJobs) => [jobData, ...prevJobs]);

      // Reset form
      setSelectedPlaylist(null);
      setCustomName('');

      // Show success message
      alert(
        "Clean playlist job created successfully! We'll process it in the background."
      );
    } catch (error) {
      console.error('Error creating clean playlist:', error);
      setError('Failed to create clean playlist. Please try again.');
    } finally {
      setProcessingPlaylist(false);
    }
  };

  const openSpotifyPlaylist = (playlistId: string) => {
    window.open(`https://open.spotify.com/playlist/${playlistId}`, '_blank');
  };

  const refreshJobs = async () => {
    if (!user) return;

    try {
      const jobsData = await getUserJobs(user.id);
      setJobs(jobsData);
    } catch (error) {
      console.error('Error refreshing jobs:', error);
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('radiowash_token');
    localStorage.removeItem('radiowash_user');
    router.push('/auth');
  };

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto py-4 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <h1 className="text-2xl font-bold text-gray-900">RadioWash</h1>
          {user && (
            <div className="flex items-center space-x-4">
              <div className="text-gray-700">
                {user.profileImageUrl && (
                  <img
                    src={user.profileImageUrl}
                    alt={user.displayName}
                    className="w-8 h-8 rounded-full inline mr-2"
                  />
                )}
                <span>{user.displayName}</span>
              </div>
              <button
                onClick={handleLogout}
                className="text-sm text-gray-500 hover:text-gray-700"
              >
                Logout
              </button>
            </div>
          )}
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        {error && (
          <div
            className="p-4 mb-6 text-sm text-red-700 bg-red-100 rounded-lg"
            role="alert"
          >
            {error}
          </div>
        )}

        {loading ? (
          <div className="flex justify-center items-center h-64">
            <p className="text-gray-500">Loading your data...</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            {/* Playlists Section */}
            <div className="lg:col-span-2 space-y-6">
              <div className="bg-white shadow rounded-lg p-6">
                <h2 className="text-xl font-semibold text-gray-900 mb-4">
                  Your Playlists
                </h2>

                <div className="bg-gray-50 p-4 rounded-lg mb-6">
                  <h3 className="text-md font-semibold text-gray-700 mb-2">
                    Create Clean Playlist
                  </h3>

                  <div className="space-y-4">
                    <div>
                      <label
                        htmlFor="playlist-select"
                        className="block text-sm font-medium text-gray-700"
                      >
                        Select a playlist
                      </label>
                      <select
                        id="playlist-select"
                        className="mt-1 block w-full p-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-green-500 focus:border-green-500"
                        value={selectedPlaylist?.id || ''}
                        onChange={(e) => {
                          const selected = playlists.find(
                            (p) => p.id === e.target.value
                          );
                          setSelectedPlaylist(selected || null);
                        }}
                      >
                        <option value="">Select a playlist</option>
                        {playlists.map((playlist) => (
                          <option key={playlist.id} value={playlist.id}>
                            {playlist.name} ({playlist.trackCount} tracks)
                          </option>
                        ))}
                      </select>
                    </div>

                    <div>
                      <label
                        htmlFor="custom-name"
                        className="block text-sm font-medium text-gray-700"
                      >
                        Custom name (optional)
                      </label>
                      <input
                        type="text"
                        id="custom-name"
                        className="mt-1 block w-full p-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-green-500 focus:border-green-500"
                        placeholder={
                          selectedPlaylist
                            ? `Clean - ${selectedPlaylist.name}`
                            : 'Clean - Playlist Name'
                        }
                        value={customName}
                        onChange={(e) => setCustomName(e.target.value)}
                      />
                    </div>

                    <button
                      onClick={handleCreateCleanPlaylist}
                      disabled={!selectedPlaylist || processingPlaylist}
                      className="w-full bg-green-600 text-white py-2 px-4 rounded-md hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50"
                    >
                      {processingPlaylist
                        ? 'Processing...'
                        : 'Create Clean Playlist'}
                    </button>
                  </div>
                </div>

                <div className="space-y-4">
                  {playlists.length === 0 ? (
                    <p className="text-gray-500">
                      No playlists found. Make sure you have playlists on
                      Spotify.
                    </p>
                  ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                      {playlists.map((playlist) => (
                        <div
                          key={playlist.id}
                          className="border rounded-lg p-4 bg-white shadow-sm hover:shadow-md transition-shadow cursor-pointer"
                          onClick={() => setSelectedPlaylist(playlist)}
                        >
                          <div className="aspect-square w-full bg-gray-200 rounded-md mb-2 overflow-hidden">
                            {playlist.imageUrl ? (
                              <img
                                src={playlist.imageUrl}
                                alt={playlist.name}
                                className="w-full h-full object-cover"
                              />
                            ) : (
                              <div className="w-full h-full flex items-center justify-center">
                                <span className="text-gray-400">No Image</span>
                              </div>
                            )}
                          </div>
                          <h3 className="font-semibold text-gray-900 truncate">
                            {playlist.name}
                          </h3>
                          <p className="text-sm text-gray-500">
                            {playlist.trackCount} tracks
                          </p>
                          <div className="flex justify-between mt-2">
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                setSelectedPlaylist(playlist);
                              }}
                              className="text-xs bg-green-100 text-green-800 px-2 py-1 rounded-md hover:bg-green-200"
                            >
                              Make Clean
                            </button>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                openSpotifyPlaylist(playlist.id);
                              }}
                              className="text-xs bg-gray-100 text-gray-800 px-2 py-1 rounded-md hover:bg-gray-200"
                            >
                              Open in Spotify
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Jobs Section */}
            <div className="space-y-6">
              <div className="bg-white shadow rounded-lg p-6">
                <div className="flex justify-between items-center mb-4">
                  <h2 className="text-xl font-semibold text-gray-900">
                    Clean Playlist Jobs
                  </h2>
                  <button
                    onClick={refreshJobs}
                    className="text-sm text-gray-500 hover:text-gray-700"
                  >
                    Refresh
                  </button>
                </div>

                {jobs.length === 0 ? (
                  <p className="text-gray-500">
                    No jobs found. Create your first clean playlist above.
                  </p>
                ) : (
                  <div className="space-y-4">
                    {jobs.map((job) => (
                      <div key={job.id} className="border rounded-lg p-4">
                        <h3 className="font-semibold text-gray-900">
                          {job.targetPlaylistName}
                        </h3>
                        <p className="text-sm text-gray-500">
                          From: {job.sourcePlaylistName}
                        </p>

                        <div className="mt-2">
                          <span
                            className={`inline-block px-2 py-1 text-xs rounded-full ${
                              job.status === 'Completed'
                                ? 'bg-green-100 text-green-800'
                                : job.status === 'Failed'
                                ? 'bg-red-100 text-red-800'
                                : job.status === 'Processing'
                                ? 'bg-blue-100 text-blue-800'
                                : 'bg-gray-100 text-gray-800'
                            }`}
                          >
                            {job.status}
                          </span>
                        </div>

                        {job.status === 'Processing' && (
                          <div className="mt-3">
                            <div className="bg-gray-200 rounded-full h-2.5">
                              <div
                                className="bg-blue-600 h-2.5 rounded-full"
                                style={{
                                  width: `${
                                    job.totalTracks > 0
                                      ? (job.processedTracks /
                                          job.totalTracks) *
                                        100
                                      : 0
                                  }%`,
                                }}
                              ></div>
                            </div>
                            <p className="text-xs text-gray-500 mt-1">
                              {job.processedTracks} of {job.totalTracks} tracks
                              processed
                            </p>
                          </div>
                        )}

                        {job.status === 'Completed' && (
                          <div className="mt-3">
                            <p className="text-sm text-gray-700">
                              Found clean versions for {job.matchedTracks} of{' '}
                              {job.totalTracks} tracks (
                              {Math.round(
                                (job.matchedTracks / job.totalTracks) * 100
                              )}
                              %)
                            </p>
                            {job.targetPlaylistId && (
                              <button
                                onClick={() =>
                                  job.targetPlaylistId &&
                                  openSpotifyPlaylist(job.targetPlaylistId)
                                }
                                className="mt-2 text-sm bg-green-100 text-green-800 px-2 py-1 rounded-md hover:bg-green-200"
                              >
                                Open in Spotify
                              </button>
                            )}
                          </div>
                        )}

                        {job.status === 'Failed' && (
                          <div className="mt-3">
                            <p className="text-sm text-red-600">
                              Error: {job.errorMessage || 'Unknown error'}
                            </p>
                          </div>
                        )}

                        <p className="text-xs text-gray-400 mt-2">
                          Created: {new Date(job.createdAt).toLocaleString()}
                        </p>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
