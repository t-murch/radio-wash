'use client';

import { useState } from 'react';
import Link from 'next/link';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ClientDate } from '@/components/ui/ClientDate';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  useSubscriptionStatus,
  useSyncConfigs,
} from '@/hooks/useSubscriptionSync';
import {
  ApiError,
  triggerManualSync,
  disableSync,
  type User,
  type PlaylistSyncConfig,
  type SyncResult,
} from '../../services/api';
import { toast } from 'sonner';
import { useMutation, useQueryClient } from '@tanstack/react-query';

const SYNC_STATUS_BADGE: Record<
  string,
  { variant: 'success' | 'error' | 'info' | 'outline'; label: string }
> = {
  completed: { variant: 'success', label: 'Up to date' },
  failed: { variant: 'error', label: 'Last check failed' },
  running: { variant: 'info', label: 'Checking now' },
};

function syncStatusBadge(status?: string) {
  return (
    SYNC_STATUS_BADGE[status?.toLowerCase() ?? ''] ?? {
      variant: 'outline' as const,
      label: 'Waiting for first check',
    }
  );
}

/**
 * The additive-only contract, stated where the feature is managed — not a
 * footnote. A user whose source playlist lost a track must be able to learn
 * here why the clean copy still has it.
 */
function AdditiveOnlyNote() {
  return (
    <p className="rounded-md border border-border bg-card px-4 py-3 text-sm text-muted-foreground">
      <span className="font-medium text-foreground">
        Auto-Sync only ever adds.
      </span>{' '}
      When new songs appear in a source playlist, their clean versions are added
      to your copy. If a song leaves the source, your copy keeps its clean
      version — Apple Music doesn&apos;t allow apps to remove tracks from a
      playlist, so nothing is ever taken out.
    </p>
  );
}

function SyncConfigCard({
  config,
  onManualSync,
  onDisable,
  isProcessing,
}: {
  config: PlaylistSyncConfig;
  onManualSync: (configId: number) => void;
  onDisable: (configId: number) => void;
  isProcessing: boolean;
}) {
  const badge = syncStatusBadge(config.lastSyncStatus);

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle className="font-display truncate text-lg">
              {config.targetPlaylistName}
            </CardTitle>
            <CardDescription className="truncate">
              From {config.sourcePlaylistName}
            </CardDescription>
          </div>
          <Badge variant={badge.variant}>{badge.label}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">
              Checks
            </dt>
            <dd className="mt-1 font-medium text-foreground capitalize">
              {config.syncFrequency}
            </dd>
          </div>
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">
              Last checked
            </dt>
            <dd className="tabular mt-1 font-medium text-foreground">
              {/* Span, not bare text: the swap to ClientDate after a manual
                  sync crashes React if Chrome Translate rewrapped the text. */}
              {config.lastSyncedAt ? (
                <ClientDate date={config.lastSyncedAt} />
              ) : (
                <span>Never</span>
              )}
            </dd>
          </div>
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">
              Next check
            </dt>
            <dd className="tabular mt-1 font-medium text-foreground">
              {config.nextScheduledSync ? (
                <ClientDate date={config.nextScheduledSync} />
              ) : (
                <span>Not scheduled</span>
              )}
            </dd>
          </div>
        </dl>

        {config.lastSyncError && (
          <p
            className="rounded-md bg-error-muted px-3 py-2 text-xs text-error"
            title={config.lastSyncError}
          >
            {config.lastSyncError}
          </p>
        )}

        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onManualSync(config.id)}
            disabled={isProcessing || !config.isActive}
          >
            Check now
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link href={`/jobs/${config.originalJobId}`}>View playlist</Link>
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onDisable(config.id)}
            disabled={isProcessing}
            className="text-muted-foreground hover:text-error"
          >
            Turn off
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function SyncDashboardClient({ initialUser }: { initialUser: User }) {
  const queryClient = useQueryClient();
  const [processingConfigId, setProcessingConfigId] = useState<number | null>(
    null
  );

  const { data: subscriptionStatus, isLoading: isLoadingSubscription } =
    useSubscriptionStatus();
  const { data: syncConfigs, isLoading: isLoadingSyncConfigs } =
    useSyncConfigs();

  const manualSyncMutation = useMutation<SyncResult, Error, number>({
    mutationFn: triggerManualSync,
    onSuccess: (result) => {
      if (result.success) {
        // Additive-only phrasing: sync never removes, so the outcome is either
        // "added N" or "nothing new" — never a removal count.
        toast.success(
          result.tracksAdded > 0
            ? `Added ${result.tracksAdded} clean ${
                result.tracksAdded === 1 ? 'version' : 'versions'
              } to your copy.`
            : 'Already up to date — nothing new to add.'
        );
      } else {
        toast.error(
          result.errorMessage || 'The check failed. Please try again.'
        );
      }
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
      setProcessingConfigId(null);
    },
    onError: (error) => {
      // 403 = plan limit reached, 400 = subscription required — both carry a
      // human-readable detail from the API.
      if (
        error instanceof ApiError &&
        (error.status === 403 || error.status === 400) &&
        error.detail
      ) {
        toast.error(error.detail);
      } else {
        toast.error('The check failed. Please try again.');
      }
      console.error('Manual sync error:', error);
      setProcessingConfigId(null);
    },
  });

  const disableSyncMutation = useMutation<{ success: boolean }, Error, number>({
    mutationFn: disableSync,
    onSuccess: () => {
      toast.success(
        'Auto-Sync is off for that playlist. Your copy stays as it is.'
      );
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
    },
    onError: (error) => {
      toast.error('Turning off Auto-Sync failed. Please try again.');
      console.error('Disable sync error:', error);
    },
  });

  const handleManualSync = (configId: number) => {
    setProcessingConfigId(configId);
    manualSyncMutation.mutate(configId);
  };

  const handleDisableSync = (configId: number) => {
    if (
      confirm(
        'Turn off Auto-Sync for this playlist? The copy keeps everything it has — you can turn syncing back on from the playlist page.'
      )
    ) {
      disableSyncMutation.mutate(configId);
    }
  };

  const isLoading = isLoadingSubscription || isLoadingSyncConfigs;

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:px-8">
        <div className="mb-6">
          <h1 className="font-display text-3xl text-foreground">Auto-Sync</h1>
          <p className="mt-2 text-muted-foreground">
            Daily checks that keep your clean copies current.
          </p>
        </div>

        <div className="mb-8">
          <AdditiveOnlyNote />
        </div>

        {isLoading ? (
          <div className="grid gap-4 md:grid-cols-2">
            <Skeleton className="h-56" />
            <Skeleton className="h-56" />
          </div>
        ) : !subscriptionStatus?.hasActiveSubscription ? (
          <Card>
            <CardHeader>
              <CardTitle className="font-display">
                Auto-Sync is part of the Sync Plan
              </CardTitle>
              <CardDescription>
                A clean copy is a snapshot of the day you made it. Auto-Sync
                checks the source daily and adds the clean versions of anything
                new, so the copy keeps up without re-running jobs.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild>
                <Link href="/subscription">See Auto-Sync</Link>
              </Button>
            </CardContent>
          </Card>
        ) : !syncConfigs?.length ? (
          <Card>
            <CardHeader>
              <CardTitle className="font-display">
                Nothing is syncing yet
              </CardTitle>
              <CardDescription>
                Auto-Sync is turned on per playlist. Open a finished clean copy
                and choose &ldquo;Turn on Auto-Sync&rdquo; — it will be checked
                daily from then on.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild variant="outline">
                <Link href="/dashboard">Go to your playlists</Link>
              </Button>
            </CardContent>
          </Card>
        ) : (
          <section aria-label="Syncing playlists">
            <div className="mb-4 flex items-center justify-between gap-3">
              <h2 className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
                Syncing <span className="tabular">{syncConfigs.length}</span>{' '}
                {syncConfigs.length === 1 ? 'playlist' : 'playlists'}
              </h2>
              <Button asChild variant="outline" size="sm">
                <Link href="/subscription">Manage billing</Link>
              </Button>
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              {syncConfigs.map((config) => (
                <SyncConfigCard
                  key={config.id}
                  config={config}
                  onManualSync={handleManualSync}
                  onDisable={handleDisableSync}
                  isProcessing={
                    processingConfigId === config.id ||
                    manualSyncMutation.isPending
                  }
                />
              ))}
            </div>
          </section>
        )}
      </main>
    </div>
  );
}
