'use client';

import { GlobalHeader } from '@/components/GlobalHeader';
import { JobCard } from '@/components/ux/JobCard';
import type { User as SupabaseUser } from '@supabase/supabase-js';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import Link from 'next/link';
import { useCallback, useState } from 'react';
import { toast } from 'sonner';
import { ProviderConnectionStatus } from '../components/ProviderConnectionStatus';
import {
  ApiError,
  createCleanPlaylistJob,
  getMe,
  getUserJobs,
  getUserPlaylists,
  Job,
  Playlist,
  User,
} from '../services/api';
import { playlistUrl } from '../lib/providers';
import { useSubscriptionStatus } from '../hooks/useSubscriptionSync';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { CURRENT_PLAN } from '@/lib/constants/pricing';
import { cn } from '@/lib/utils';

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

  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const [customName, setCustomName] = useState('');
  const [appleConnected, setAppleConnected] = useState(false);

  const onConnectionChange = useCallback(
    (connected: boolean) => setAppleConnected(connected),
    []
  );

  const { data: me } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    initialData: initialMe,
  });

  const { data: playlistsResponse } = useQuery<
    Playlist[] | { error: string; message: string; playlists: Playlist[] }
  >({
    queryKey: ['playlists'],
    queryFn: () => getUserPlaylists('apple_music'),
    enabled: !!me && appleConnected,
    placeholderData: initialPlaylists,
  });

  // The playlist endpoint degrades to { error, playlists: [] } instead of failing
  // outright when Apple is unreachable.
  const playlists: Playlist[] = Array.isArray(playlistsResponse)
    ? playlistsResponse
    : playlistsResponse?.playlists || [];

  const { data: jobs = [] } = useQuery<Job[]>({
    queryKey: ['jobs'],
    queryFn: getUserJobs,
    enabled: !!me,
    initialData: initialJobs,
  });

  const { data: subscriptionStatus } = useSubscriptionStatus();

  const selectedPlaylist = playlists.find((p) => p.id === selectedPlaylistId);

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName?: string }) =>
      createCleanPlaylistJob(me!.id, {
        sourcePlaylistId: vars.sourcePlaylistId,
        targetPlaylistName: vars.targetName,
        provider: 'apple_music',
        swapExplicitForClean: true,
      }),
    onSuccess: (job) => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] });
      queryClient.invalidateQueries({ queryKey: ['playlists'] });
      toast.success(
        `Working on "${
          job.targetPlaylistName || job.sourcePlaylistName
        }" — follow it under Jobs.`
      );
      setSelectedPlaylistId('');
      setCustomName('');
    },
    onError: (error) => {
      console.error('Creating the clean-copy job failed:', error);
    },
  });

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedPlaylist || !me) return;
    // An empty name accepts the server default ("Clean - <source name>").
    createJobMutation.mutate({
      sourcePlaylistId: selectedPlaylist.id,
      targetName: customName.trim() || undefined,
    });
  };

  const submitError = createJobMutation.error
    ? createJobMutation.error instanceof ApiError
      ? createJobMutation.error.message
      : 'Something went wrong starting the job. Please try again.'
    : null;

  // Only pitch Auto-Sync to someone we know is not already paying for it —
  // while the subscription query is in flight, say nothing.
  const showSyncPitch =
    jobs.some((job) => job.status === 'Completed') &&
    subscriptionStatus !== undefined &&
    !subscriptionStatus.hasActiveSubscription;

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader user={me} />
      <main className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
          <div className="space-y-8 lg:col-span-2">
            <ProviderConnectionStatus
              provider="apple_music"
              onConnectionChange={onConnectionChange}
            />

            <Card>
              <CardHeader>
                <CardTitle className="font-display">
                  Make a clean copy
                </CardTitle>
                <CardDescription>
                  Pick a playlist and RadioWash builds a copy with radio edits
                  swapped in where they exist. Your original is never touched.
                </CardDescription>
              </CardHeader>
              <CardContent>
                {!appleConnected ? (
                  <p className="py-4 text-muted-foreground">
                    Connect Apple Music above and your playlists will appear
                    here.
                  </p>
                ) : (
                  <form onSubmit={handleSubmit} className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="source-playlist">Playlist</Label>
                      <select
                        id="source-playlist"
                        value={selectedPlaylistId}
                        onChange={(e) => setSelectedPlaylistId(e.target.value)}
                        className={cn(
                          'flex h-10 w-full rounded-md border border-input bg-input px-3 py-2 text-base text-foreground md:text-sm',
                          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 ring-offset-background'
                        )}
                      >
                        <option value="">Choose a playlist…</option>
                        {playlists.map((p) => (
                          <option key={p.id} value={p.id}>
                            {p.name}
                            {p.trackCount > 0
                              ? ` (${p.trackCount} tracks)`
                              : ''}
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="copy-name">
                        Name for the copy{' '}
                        <span className="font-normal text-muted-foreground">
                          (optional)
                        </span>
                      </Label>
                      <Input
                        id="copy-name"
                        type="text"
                        placeholder={
                          selectedPlaylist
                            ? `Clean - ${selectedPlaylist.name}`
                            : 'Clean - <playlist name>'
                        }
                        value={customName}
                        onChange={(e) => setCustomName(e.target.value)}
                      />
                    </div>

                    {submitError && (
                      <Alert variant="error">
                        <AlertDescription>{submitError}</AlertDescription>
                      </Alert>
                    )}

                    <Button
                      type="submit"
                      disabled={
                        !selectedPlaylistId || createJobMutation.isPending
                      }
                      className="w-full sm:w-auto"
                    >
                      {createJobMutation.isPending
                        ? 'Starting…'
                        : 'Make the clean copy'}
                    </Button>
                  </form>
                )}
              </CardContent>
            </Card>

            {appleConnected && (
              <section
                aria-labelledby="playlists-heading"
                className="space-y-4"
              >
                <h2
                  id="playlists-heading"
                  className="font-display text-lg font-semibold text-foreground"
                >
                  Your playlists
                </h2>
                {playlists.length === 0 ? (
                  <p className="text-muted-foreground">
                    No playlists in your library yet. Make one in Apple Music
                    and it will show up here.
                  </p>
                ) : (
                  <ul className="grid max-h-[65vh] grid-cols-1 gap-3 overflow-y-auto md:grid-cols-2 md:gap-4 xl:grid-cols-3">
                    {playlists.map((playlist, idx) => (
                      <PlaylistCard
                        key={playlist.id}
                        playlist={playlist}
                        selected={playlist.id === selectedPlaylistId}
                        onSelect={() => setSelectedPlaylistId(playlist.id)}
                        priority={idx < 6}
                      />
                    ))}
                  </ul>
                )}
              </section>
            )}
          </div>

          <div className="min-w-0 space-y-6 lg:col-span-1">
            {showSyncPitch && (
              <Card>
                <CardHeader>
                  <CardTitle className="font-display">
                    Keep your copies current
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <p className="text-sm text-muted-foreground">
                    When you add songs to a playlist you&apos;ve cleaned,
                    Auto-Sync adds their clean versions to the copy every day —
                    no manual runs. {CURRENT_PLAN.MARKETING_PRICE}/month, up to{' '}
                    {CURRENT_PLAN.FEATURES.MAX_PLAYLISTS} playlists.
                  </p>
                  <Button variant="outline" size="sm" asChild>
                    <Link href="/subscription">See Auto-Sync</Link>
                  </Button>
                </CardContent>
              </Card>
            )}

            <section aria-labelledby="jobs-heading" className="space-y-4">
              <h2
                id="jobs-heading"
                className="font-display text-lg font-semibold text-foreground"
              >
                Jobs
              </h2>
              <div className="max-h-[60vh] space-y-4 overflow-y-auto pr-2">
                {jobs.length > 0 ? (
                  jobs.map((job) => <JobCard key={job.id} job={job} />)
                ) : (
                  <p className="py-4 text-muted-foreground">
                    Nothing yet. Your first clean copy will show up here.
                  </p>
                )}
              </div>
            </section>
          </div>
        </div>
      </main>
    </div>
  );
}

/**
 * One card serves both viewports: a horizontal row on mobile, a vertical tile
 * from md up. The former separate desktop/mobile trees rendered every playlist
 * twice with only CSS hiding one copy.
 */
function PlaylistCard({
  playlist,
  selected,
  onSelect,
  priority,
}: {
  playlist: Playlist;
  selected: boolean;
  onSelect: () => void;
  priority: boolean;
}) {
  const url = playlistUrl('apple_music', playlist.id);

  return (
    <li
      className={cn(
        'rounded-md border bg-card p-3 transition-colors',
        selected ? 'border-primary ring-1 ring-primary' : 'border-border'
      )}
    >
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={selected}
        className="flex w-full items-center gap-3 text-left md:flex-col md:items-stretch"
      >
        <div className="relative size-14 shrink-0 overflow-hidden rounded-[3px] bg-muted md:aspect-square md:size-auto md:w-full">
          {playlist.imageUrl ? (
            <Image
              src={playlist.imageUrl}
              alt=""
              fill
              sizes="(min-width: 768px) 33vw, 56px"
              className="object-cover"
              priority={priority}
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center">
              <span className="text-xs text-muted-foreground">No image</span>
            </div>
          )}
        </div>
        <div className="min-w-0 flex-1 md:mt-2">
          <h3
            className="truncate font-medium text-foreground"
            title={playlist.name}
          >
            {playlist.name}
          </h3>
          {playlist.trackCount > 0 && (
            <p className="tabular text-sm text-muted-foreground">
              {playlist.trackCount} tracks
            </p>
          )}
        </div>
      </button>
      {/* Library playlists (p.xxx ids) have no public URL, so this link is the
          exception, not the rule. */}
      {url && (
        <a
          href={url}
          target="_blank"
          rel="noopener noreferrer"
          className="mt-2 inline-block text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground"
        >
          Open in Apple Music
        </a>
      )}
    </li>
  );
}
