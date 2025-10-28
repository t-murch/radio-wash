'use client';

import { GlobalHeader } from '@/components/GlobalHeader';
import { JobCard } from '@/components/ux/JobCard';
import type { User as SupabaseUser } from '@supabase/supabase-js';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import { useState } from 'react';
import { SpotifyConnectionStatus } from '../components/SpotifyConnectionStatus';
import {
  createCleanPlaylistJob,
  getMe,
  getUserJobs,
  getUserPlaylists,
  Job,
  Playlist,
  User,
} from '../services/api';
import { useSubscriptionStatus } from '../hooks/useSubscriptionSync';
import { Button } from '@/components/ui/button';
import { useRouter } from 'next/navigation';
import { CURRENT_PLAN, FEATURE_DESCRIPTIONS } from '@/lib/constants/pricing';

export function DashboardClient({
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
    placeholderData: initialPlaylists,
  });

  // Handle the response structure that includes error and playlists fields
  const playlists: Playlist[] = Array.isArray(playlistsResponse)
    ? playlistsResponse
    : playlistsResponse?.playlists || [];

  const { data: jobs = [], refetch: refetchJobs } = useQuery<Job[]>({
    queryKey: ['jobs'],
    queryFn: getUserJobs,
    enabled: !!me,
    initialData: initialJobs,
  });

  const { data: subscriptionStatus } = useSubscriptionStatus();

  const openSpotifyPlaylist = (playlistId: string) => {
    window.open(`https://open.spotify.com/playlist/${playlistId}`, '_blank');
  };

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName: string }) =>
      createCleanPlaylistJob(me!.id, vars.sourcePlaylistId, vars.targetName),
    onSuccess: (newJob) => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] });
      queryClient.invalidateQueries({ queryKey: ['playlists'] });
      // Force immediate refetch
      setTimeout(() => refetchJobs(), 500);
      setSelectedPlaylistId('');
      setCustomName('');
    },
    onError: (error) => {
      console.error('[Dashboard Debug] Job creation failed:', error);
    },
  });

  const handleCreatePlaylist = () => {
    const selected = playlists.find((p) => p.id === selectedPlaylistId);
    if (!selected || !me) return;
    const targetName = customName.trim() || `Clean - ${selected.name}`;
    createJobMutation.mutate({ sourcePlaylistId: selected.id, targetName });
  };

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader user={me} />
      <main className="max-w-7xl mx-auto py-8 px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <div className="lg:col-span-2 space-y-8">
            <SpotifyConnectionStatus onConnectionChange={setSpotifyConnected} />
            <div className="bg-card border rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-foreground mb-4">
                Create a Clean Playlist
              </h2>
              {!spotifyConnected ? (
                <div className="text-center py-8">
                  <div className="w-16 h-16 mx-auto mb-4 bg-muted rounded-full flex items-center justify-center">
                    <svg
                      className="w-8 h-8 text-muted-foreground"
                      viewBox="0 0 24 24"
                      fill="currentColor"
                    >
                      <path d="M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.84-.179-.84-.66 0-.36.24-.66.54-.78 4.56-1.021 8.52-.6 11.64 1.32.36.18.48.66.24 1.021zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.481.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.42 1.56-.299.421-1.02.599-1.559.3z" />
                    </svg>
                  </div>
                  <h3 className="text-lg font-medium text-foreground mb-2">
                    Connect Spotify to Get Started
                  </h3>
                  <p className="text-muted-foreground mb-4">
                    Connect your Spotify account to access your playlists and
                    create clean versions.
                  </p>
                </div>
              ) : (
                <div className="space-y-4">
                  <select
                    value={selectedPlaylistId}
                    onChange={(e) => setSelectedPlaylistId(e.target.value)}
                    className="block w-full p-3 border rounded-md"
                  >
                    <option value="">-- Choose a playlist --</option>
                    {playlists.map((p, idx) => (
                      <option key={idx} value={p.id}>
                        {p.name} ({p.trackCount} tracks)
                      </option>
                    ))}
                  </select>
                  <input
                    type="text"
                    placeholder="New Playlist Name (Optional)"
                    value={customName}
                    onChange={(e) => setCustomName(e.target.value)}
                    className="block w-full p-3 border rounded-md"
                  />
                  <button
                    onClick={handleCreatePlaylist}
                    disabled={
                      !selectedPlaylistId || createJobMutation.isPending
                    }
                    className="w-full bg-success text-success-foreground py-3 rounded-md hover:bg-success-hover disabled:opacity-50"
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
                  <p className="text-muted-foreground">
                    No playlists found. Make sure you have playlists on Spotify.
                  </p>
                ) : (
                  <div className="space-y-4 max-h-[65vh] overflow-y-auto overflow-x-hidden">
                    {/* Desktop view - grid layout */}
                    <div className="hidden md:grid md:grid-cols-2 lg:grid-cols-2 xl:grid-cols-3 gap-4">
                      {playlists.map((playlist, idx) => (
                        <div
                          key={idx}
                          className="border rounded-lg p-4 bg-card shadow-sm hover:shadow-md transition-shadow cursor-pointer min-w-0"
                          onClick={() => setSelectedPlaylistId(playlist.id)}
                        >
                          <div className="aspect-square w-full bg-muted rounded-md mb-2 overflow-hidden">
                            {playlist.imageUrl ? (
                              <Image
                                src={playlist.imageUrl}
                                alt={playlist.name}
                                className="object-cover w-full h-full"
                                width={200}
                                height={200}
                                priority={idx < 7}
                              />
                            ) : (
                              <div className="w-full h-full flex items-center justify-center">
                                <span className="text-muted-foreground">
                                  No Image
                                </span>
                              </div>
                            )}
                          </div>
                          <h3
                            className="font-semibold text-foreground truncate"
                            title={playlist.name}
                          >
                            {playlist.name}
                          </h3>
                          <p className="text-sm text-muted-foreground">
                            {playlist.trackCount} tracks
                          </p>
                          <div className="flex flex-col sm:flex-row gap-2 mt-2">
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                setSelectedPlaylistId(playlist.id);
                              }}
                              className="text-xs bg-success-muted text-success px-2 py-1 rounded-md hover:bg-success/20 flex-1 sm:flex-none"
                            >
                              Make Clean
                            </button>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                openSpotifyPlaylist(playlist.id);
                              }}
                              className="text-xs bg-muted text-foreground px-2 py-1 rounded-md hover:bg-muted/80 flex-1 sm:flex-none"
                            >
                              Open in Spotify
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>

                    {/* Mobile view - list layout */}
                    <div className="md:hidden space-y-3">
                      {playlists.map((playlist, idx) => (
                        <div
                          key={idx}
                          className="bg-card border rounded-lg p-4 shadow-sm"
                          onClick={() => setSelectedPlaylistId(playlist.id)}
                        >
                          <div className="flex items-center gap-4">
                            {/* Playlist Image */}
                            <div className="w-16 h-16 bg-muted rounded-lg overflow-hidden flex-shrink-0">
                              {playlist.imageUrl ? (
                                <Image
                                  src={playlist.imageUrl}
                                  alt={playlist.name}
                                  className="object-cover w-full h-full"
                                  width={64}
                                  height={64}
                                  priority={idx < 7}
                                />
                              ) : (
                                <div className="w-full h-full flex items-center justify-center">
                                  <span className="text-xs text-muted-foreground">
                                    No Image
                                  </span>
                                </div>
                              )}
                            </div>

                            {/* Playlist Info and Buttons */}
                            <div className="flex-1 min-w-0">
                              <div className="mb-2">
                                <h3
                                  className="font-semibold text-foreground text-base truncate"
                                  title={playlist.name}
                                >
                                  {playlist.name}
                                </h3>
                                <p className="text-sm text-muted-foreground">
                                  🎵 {playlist.trackCount} tracks
                                </p>
                              </div>

                              <div className="flex flex-col xs:flex-row gap-2">
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    setSelectedPlaylistId(playlist.id);
                                  }}
                                  className="px-3 py-2 bg-success text-success-foreground text-sm font-medium rounded-lg hover:bg-success-hover transition-colors flex-1 xs:flex-none"
                                >
                                  Make Clean
                                </button>
                                <button
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    openSpotifyPlaylist(playlist.id);
                                  }}
                                  className="px-3 py-2 bg-muted text-foreground text-sm font-medium rounded-lg hover:bg-muted/80 transition-colors flex-1 xs:flex-none"
                                >
                                  Open in Spotify
                                </button>
                              </div>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
          <div className="lg:col-span-1 space-y-6 min-w-0">
            {/* Sync Discovery Section */}
            {jobs.some((job) => job.status === 'Completed') &&
              !subscriptionStatus?.hasActiveSubscription && (
                <div className="bg-gradient-to-r from-brand/5 to-info/5 border border-brand/30 rounded-lg p-4 sm:p-6 overflow-hidden">
                  <div className="flex items-start space-x-3 sm:space-x-4">
                    <div className="flex-shrink-0">
                      <div className="w-10 h-10 sm:w-12 sm:h-12 bg-brand/20 rounded-full flex items-center justify-center">
                        <span className="text-xl sm:text-2xl">🔄</span>
                      </div>
                    </div>
                    <div className="flex-1 min-w-0">
                      <h3 className="text-base sm:text-lg font-semibold text-foreground mb-2">
                        Tired of Manual Updates?
                      </h3>
                      <p className="text-sm sm:text-base text-muted-foreground mb-3 sm:mb-4">
                        You have completed playlists! Enable Auto-Sync to keep
                        them updated automatically when your source playlists
                        change. Never run manual jobs again.
                      </p>
                      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2 gap-2 mb-3 sm:mb-4">
                        <span className="px-2 sm:px-3 py-1 bg-card text-xs sm:text-sm rounded-full border text-center">
                          ⏰ Daily automatic sync
                        </span>
                        <span className="px-2 sm:px-3 py-1 bg-card text-xs sm:text-sm rounded-full border text-center">
                          {FEATURE_DESCRIPTIONS.MONTHLY_PRICE(CURRENT_PLAN.MARKETING_PRICE)}
                        </span>
                        <span className="px-2 sm:px-3 py-1 bg-white dark:bg-gray-800 text-xs sm:text-sm rounded-full border text-center sm:col-span-2 lg:col-span-1 xl:col-span-2">
                          🎯 Up to 10 playlists
                        </span>
                      </div>
                      <div className="flex flex-col gap-2 sm:gap-3">
                        <Button
                          onClick={() => router.push('/subscription')}
                          className="bg-brand hover:bg-brand-hover text-brand-foreground text-xs sm:text-sm w-full px-2 sm:px-3"
                          size="sm"
                        >
                          <span className="sm:hidden">Auto-Sync</span>
                          <span className="hidden sm:inline">Learn More About Auto-Sync</span>
                        </Button>
                        <Button
                          variant="outline"
                          onClick={() => {
                            const completedJob = jobs.find(
                              (job) => job.status === 'Completed'
                            );
                            if (completedJob) {
                              router.push(`/jobs/${completedJob.id}`);
                            }
                          }}
                          className="text-sm sm:text-base w-full"
                          size="sm"
                        >
                          See Sync Options
                        </Button>
                      </div>
                    </div>
                  </div>
                </div>
              )}

            <div className="bg-card border rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-foreground mb-4">
                Job Status
              </h2>
              <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-2">
                {jobs.length > 0 ? (
                  jobs.map((job) => <JobCard key={job.id} job={job} />)
                ) : (
                  <p className="text-muted-foreground text-center py-4">
                    No jobs yet.
                  </p>
                )}
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}
