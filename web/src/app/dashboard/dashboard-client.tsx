'use client';

import { GlobalHeader } from '@/components/GlobalHeader';
import { JobCard } from '@/components/ux/JobCard';
import type { User as SupabaseUser } from '@supabase/supabase-js';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import { useCallback, useState } from 'react';
import { ProviderConnectionStatus } from '../components/ProviderConnectionStatus';
import {
  createCleanPlaylistJob,
  getMe,
  getUserJobs,
  getUserPlaylists,
  Job,
  MusicProvider,
  Playlist,
  User,
} from '../services/api';
import { playlistUrl, PROVIDER_LABELS } from '../lib/providers';
import { useSubscriptionStatus } from '../hooks/useSubscriptionSync';
import { Button } from '@/components/ui/button';
import { useRouter } from 'next/navigation';
import { CURRENT_PLAN, FEATURE_DESCRIPTIONS } from '@/lib/constants/pricing';

// NOTE: the cross-provider "copy to the other service" affordance below is now
// unreachable — there is only one provider, so a copy target can only be the
// source. Phase 6 rewrites this screen and removes it outright; until then the
// destination is pinned to the active provider so the code still type-checks.

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

  const [activeProvider, setActiveProvider] = useState<MusicProvider>('apple_music');
  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const [customName, setCustomName] = useState('');
  // 'clean' = same-service clean job; 'copy' = cross-service copy to the other provider.
  const [destination, setDestination] = useState<'clean' | 'copy'>('clean');
  const [swapExplicit, setSwapExplicit] = useState(true);
  const [connections, setConnections] = useState<Record<MusicProvider, boolean>>({
    apple_music: false,
  });

  const onAppleConnectionChange = useCallback(
    (connected: boolean) =>
      setConnections((prev) => ({ ...prev, apple_music: connected })),
    []
  );

  // Use React Query to manage data, with initial data from the server
  const { data: me } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    initialData: initialMe,
  });

  const { data: playlistsResponse } = useQuery<
    Playlist[] | { error: string; message: string; playlists: Playlist[] }
  >({
    queryKey: ['playlists', activeProvider],
    queryFn: () => getUserPlaylists(activeProvider),
    enabled: !!me && connections[activeProvider],
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

  const activeLabel = PROVIDER_LABELS[activeProvider];
  const targetProvider = activeProvider;
  const targetLabel = PROVIDER_LABELS[targetProvider];
  const activeConnected = connections[activeProvider];
  // A copy needs the destination account connected too.
  const copyBlocked = destination === 'copy' && !connections[targetProvider];

  const openPlaylist = (playlistId: string) => {
    const url = playlistUrl(activeProvider, playlistId);
    if (url) window.open(url, '_blank');
  };

  const selectProvider = (provider: MusicProvider) => {
    setActiveProvider(provider);
    setSelectedPlaylistId('');
    setDestination('clean');
  };

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName?: string }) =>
      createCleanPlaylistJob(me!.id, {
        sourcePlaylistId: vars.sourcePlaylistId,
        targetPlaylistName: vars.targetName,
        provider: activeProvider,
        targetProvider,
        swapExplicitForClean: destination === 'copy' ? swapExplicit : true,
      }),
    onSuccess: () => {
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
    // Leave the name empty to accept the server default ("Clean - X", or the source name
    // for faithful copies).
    createJobMutation.mutate({
      sourcePlaylistId: selected.id,
      targetName: customName.trim() || undefined,
    });
  };

  const submitLabel = () => {
    if (createJobMutation.isPending) return 'Working on it...';
    if (destination === 'copy') {
      return swapExplicit
        ? `Copy Clean Version to ${targetLabel}`
        : `Copy to ${targetLabel}`;
    }
    return 'Create Clean Version';
  };

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader user={me} />
      <main className="max-w-7xl mx-auto py-8 px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <div className="lg:col-span-2 space-y-8">
            <ProviderConnectionStatus
              provider="apple_music"
              onConnectionChange={onAppleConnectionChange}
            />

            <div className="bg-card border rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-foreground mb-4">
                Clean or Copy a Playlist
              </h2>
              {!activeConnected ? (
                <div className="text-center py-8">
                  <h3 className="text-lg font-medium text-foreground mb-2">
                    Connect {activeLabel} to Get Started
                  </h3>
                  <p className="text-muted-foreground mb-4">
                    Connect your {activeLabel} account above to access your
                    playlists.
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
                        {p.name}
                        {p.trackCount > 0 ? ` (${p.trackCount} tracks)` : ''}
                      </option>
                    ))}
                  </select>

                  <div className="flex flex-col sm:flex-row gap-2">
                    <label className="flex-1 flex items-center gap-2 p-3 border rounded-md cursor-pointer">
                      <input
                        type="radio"
                        name="destination"
                        checked={destination === 'clean'}
                        onChange={() => setDestination('clean')}
                      />
                      <span className="text-sm">
                        Clean it on {activeLabel}
                      </span>
                    </label>
                    <label className="flex-1 flex items-center gap-2 p-3 border rounded-md cursor-pointer">
                      <input
                        type="radio"
                        name="destination"
                        checked={destination === 'copy'}
                        onChange={() => setDestination('copy')}
                      />
                      <span className="text-sm">
                        Copy to {PROVIDER_LABELS[activeProvider]}
                      </span>
                    </label>
                  </div>

                  {destination === 'copy' && (
                    <label className="flex items-center gap-2 px-1 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={swapExplicit}
                        onChange={(e) => setSwapExplicit(e.target.checked)}
                      />
                      <span className="text-sm text-muted-foreground">
                        Swap explicit tracks for clean versions during the copy
                      </span>
                    </label>
                  )}

                  {copyBlocked && (
                    <p className="text-sm text-warning">
                      Connect your {targetLabel} account above to copy playlists
                      there.
                    </p>
                  )}

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
                      !selectedPlaylistId ||
                      copyBlocked ||
                      createJobMutation.isPending
                    }
                    className="w-full bg-success text-success-foreground py-3 rounded-md hover:bg-success-hover disabled:opacity-50"
                  >
                    {submitLabel()}
                  </button>
                </div>
              )}
            </div>

            {activeConnected && (
              <div className="space-y-4">
                {playlists.length === 0 ? (
                  <p className="text-muted-foreground">
                    No playlists found. Make sure you have playlists on{' '}
                    {activeLabel}.
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
                          {playlist.trackCount > 0 && (
                            <p className="text-sm text-muted-foreground">
                              {playlist.trackCount} tracks
                            </p>
                          )}
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
                            {playlistUrl(activeProvider, playlist.id) && (
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openPlaylist(playlist.id);
                                }}
                                className="text-xs bg-muted text-foreground px-2 py-1 rounded-md hover:bg-muted/80 flex-1 sm:flex-none"
                              >
                                Open in {activeLabel}
                              </button>
                            )}
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
                                {playlist.trackCount > 0 && (
                                  <p className="text-sm text-muted-foreground">
                                    🎵 {playlist.trackCount} tracks
                                  </p>
                                )}
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
                                {playlistUrl(activeProvider, playlist.id) && (
                                  <button
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      openPlaylist(playlist.id);
                                    }}
                                    className="px-3 py-2 bg-muted text-foreground text-sm font-medium rounded-lg hover:bg-muted/80 transition-colors flex-1 xs:flex-none"
                                  >
                                    Open in {activeLabel}
                                  </button>
                                )}
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
                <div className="bg-gradient-to-r from-brand/10 to-info/10 border border-brand/50 rounded-lg p-4 sm:p-6 overflow-hidden">
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
                        <span className="px-2 sm:px-3 py-1 bg-card text-xs sm:text-sm rounded-full border text-center sm:col-span-2 lg:col-span-1 xl:col-span-2">
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
