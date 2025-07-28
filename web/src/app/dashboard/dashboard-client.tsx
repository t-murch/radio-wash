'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { Suspense, useCallback, useEffect, useState } from 'react';
import { createClient } from '@/lib/supabase/client';
import {
  createCleanPlaylistJob,
  getUserJobs,
  getUserPlaylists,
  getMe,
  Job,
  Playlist,
  User,
} from '../services/api';
import { JobCard } from '@/components/ux/JobCard';
import { SpotifyConnectionStatus } from '../components/SpotifyConnectionStatus';
import type { User as SupabaseUser } from '@supabase/supabase-js';

export function DashboardClient({
  serverUser,
  initialMe,
  initialPlaylists,
  initialJobs,
}: {
  serverUser: SupabaseUser;
  initialMe: User;
  initialPlaylists:
    | Playlist[]
    | { error: string; message: string; playlists: Playlist[] };
  initialJobs: Job[];
}) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const supabase = createClient();

  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const [customName, setCustomName] = useState('');
  const [spotifyConnected, setSpotifyConnected] = useState(true); // Default to true, will be updated by component

  // Use React Query to manage data, with initial data from the server
  const { data: me } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    initialData: initialMe,
  });

  const { data: playlistsResponse } = useQuery<
    Playlist[] | { error: string; message: string; playlists: Playlist[] }
  >({
    queryKey: ['playlists'],
    queryFn: getUserPlaylists,
    enabled: !!me,
    initialData: initialPlaylists,
  });

  // Handle the response structure that includes error and playlists fields
  const playlists: Playlist[] = Array.isArray(playlistsResponse)
    ? playlistsResponse
    : playlistsResponse?.playlists || [];

  const { data: jobs = [] } = useQuery<Job[]>({
    queryKey: ['jobs'],
    queryFn: getUserJobs,
    enabled: !!me,
    initialData: initialJobs,
  });

  const openSpotifyPlaylist = (playlistId: string) => {
    window.open(`https://open.spotify.com/playlist/${playlistId}`, '_blank');
  };

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName: string }) =>
      createCleanPlaylistJob(me!.id, vars.sourcePlaylistId, vars.targetName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] });
      setSelectedPlaylistId('');
      setCustomName('');
    },
  });

  // Set up Supabase Realtime subscription for job updates
  useEffect(() => {
    if (!me) return;

    const channel = supabase
      .channel('db-jobs')
      .on(
        'postgres_changes',
        {
          event: '*',
          schema: 'public',
          table: 'CleanPlaylistJobs',
          filter: `user_id=eq.${me.id}`,
        },
        (payload) => {
          queryClient.invalidateQueries({ queryKey: ['jobs'] });
        }
      )
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, [supabase, queryClient, me]);

  const handleCreatePlaylist = () => {
    const selected = playlists.find((p) => p.id === selectedPlaylistId);
    if (!selected || !me) return;
    const targetName = customName.trim() || `Clean - ${selected.name}`;
    createJobMutation.mutate({ sourcePlaylistId: selected.id, targetName });
  };

  const handleLogout = async () => {
    await supabase.auth.signOut();
    router.refresh(); // Refresh the page to trigger redirect
  };

  return (
    <div className="min-h-screen bg-gray-100">
      <header className="bg-white shadow-sm sticky top-0 z-10">
        <div className="max-w-7xl mx-auto py-3 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <div className="flex items-center space-x-3">
            <h1 className="text-2xl font-bold text-gray-800">RadioWash</h1>
            <div className="flex items-center space-x-1"></div>
          </div>
          {me && (
            <div className="flex items-center space-x-4">
              <span className="text-gray-600 hidden sm:inline">
                Welcome, {me.displayName}
              </span>
              <Image
                src={me.profileImageUrl || `/user.svg`}
                alt="User Profile"
                className="rounded-full"
                width={40}
                height={40}
                priority
              />
              <button
                onClick={handleLogout}
                className="text-sm font-medium text-gray-500 hover:text-gray-700"
              >
                Logout
              </button>
            </div>
          )}
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-8 px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-5 gap-8">
          <div className="lg:col-span-3 space-y-8">
            <SpotifyConnectionStatus onConnectionChange={setSpotifyConnected} />
            <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-gray-900 mb-4">
                Create a Clean Playlist
              </h2>
              {!spotifyConnected ? (
                <div className="text-center py-8">
                  <div className="w-16 h-16 mx-auto mb-4 bg-gray-100 rounded-full flex items-center justify-center">
                    <svg
                      className="w-8 h-8 text-gray-400"
                      viewBox="0 0 24 24"
                      fill="currentColor"
                    >
                      <path d="M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.84-.179-.84-.66 0-.36.24-.66.54-.78 4.56-1.021 8.52-.6 11.64 1.32.36.18.48.66.24 1.021zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.481.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.42 1.56-.299.421-1.02.599-1.559.3z" />
                    </svg>
                  </div>
                  <h3 className="text-lg font-medium text-gray-900 mb-2">
                    Connect Spotify to Get Started
                  </h3>
                  <p className="text-gray-600 mb-4">
                    Connect your Spotify account to access your playlists and
                    create clean versions.
                  </p>
                </div>
              ) : (
                <div className="space-y-4">
                  <select
                    value={selectedPlaylistId}
                    onChange={(e) => setSelectedPlaylistId(e.target.value)}
                    className="block w-full p-3 border border-gray-300 rounded-md"
                  >
                    <option value="">-- Choose a playlist --</option>
                    {playlists.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} ({p.trackCount} tracks)
                      </option>
                    ))}
                  </select>
                  <input
                    type="text"
                    placeholder="New Playlist Name (Optional)"
                    value={customName}
                    onChange={(e) => setCustomName(e.target.value)}
                    className="block w-full p-3 border border-gray-300 rounded-md"
                  />
                  <button
                    onClick={handleCreatePlaylist}
                    disabled={
                      !selectedPlaylistId || createJobMutation.isPending
                    }
                    className="w-full bg-green-600 text-white py-3 rounded-md hover:bg-green-700 disabled:opacity-50"
                  >
                    {createJobMutation.isPending
                      ? 'Working on it...'
                      : 'Create Clean Version'}
                  </button>
                </div>
              )}
            </div>

            {spotifyConnected && (
              <div className="space-y-4">
                {playlists.length === 0 ? (
                  <p className="text-gray-500">
                    No playlists found. Make sure you have playlists on Spotify.
                  </p>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                    {playlists.map((playlist, idx) => (
                      <div
                        key={idx}
                        className="border rounded-lg p-4 bg-white shadow-sm hover:shadow-md transition-shadow cursor-pointer"
                        onClick={() => setSelectedPlaylistId(playlist.id)}
                      >
                        <div className="aspect-square w-full bg-gray-200 rounded-md mb-2 overflow-hidden">
                          {playlist.imageUrl ? (
                            <Image
                              src={playlist.imageUrl}
                              alt={playlist.name}
                              className="object-cover"
                              width={200}
                              height={200}
                              priority={idx < 7}
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
                              setSelectedPlaylistId(playlist.id);
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
            )}
          </div>
          <div className="lg:col-span-2">
            <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-gray-900 mb-4">
                Job Status
              </h2>
              <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-2">
                {jobs.length > 0 ? (
                  jobs.map((job) => <JobCard key={job.id} job={job} />)
                ) : (
                  <p className="text-gray-500 text-center py-4">No jobs yet.</p>
                )}
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}
