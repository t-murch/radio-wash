'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { toast } from 'sonner';
import { trackAutoSyncEnabled } from '@/lib/analytics';

import { ApiError, getJobDetails, Job, User } from '../../services/api';
import TrackMappings from '@/components/ux/TrackMappings';
import { jobTypeLabel, playlistUrl, providerLabel } from '@/lib/providers';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { ClientDate } from '@/components/ui/ClientDate';
import { Progress } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import {
  useEnableSyncForJob,
  useSubscriptionStatus,
  useSyncConfigs,
} from '@/hooks/useSubscriptionSync';
import { CURRENT_PLAN } from '@/lib/constants/pricing';

const STATUS_BADGE_VARIANT: Record<
  string,
  'success' | 'error' | 'info' | 'outline'
> = {
  Completed: 'success',
  Failed: 'error',
  Processing: 'info',
  Pending: 'outline',
};

export function JobDetailsClient({
  initialMe,
  initialJob,
  jobId,
}: {
  initialMe: User;
  initialJob: Job;
  jobId: number;
}) {
  // The dashboard cards get live SignalR progress; here a light poll is enough,
  // and it stops itself once the job settles.
  const { data: job } = useQuery<Job>({
    queryKey: ['job', initialMe.id, jobId],
    queryFn: () => getJobDetails(initialMe.id, jobId),
    initialData: initialJob,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'Pending' || status === 'Processing' ? 4000 : false;
    },
  });

  if (!job) return null;

  const progressPercent =
    job.totalTracks > 0
      ? Math.round((job.processedTracks / job.totalTracks) * 100)
      : 0;

  const targetUrl = job.targetPlaylistId
    ? playlistUrl(job.targetProvider, job.targetPlaylistId)
    : null;

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialMe}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8">
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <CardTitle className="font-display text-2xl">
                  {job.targetPlaylistName || job.sourcePlaylistName}
                </CardTitle>
                <CardDescription className="mt-1">
                  From {job.sourcePlaylistName}
                </CardDescription>
                <p className="mt-1 text-sm text-muted-foreground">
                  {jobTypeLabel(job)}
                  {' · '}
                  {job.jobType === 'copy'
                    ? `${providerLabel(job.provider)} → ${providerLabel(
                        job.targetProvider
                      )}`
                    : providerLabel(job.provider)}
                  {' · '}
                  Started <ClientDate date={job.createdAt} />
                </p>
              </div>
              <Badge variant={STATUS_BADGE_VARIANT[job.status] ?? 'outline'}>
                {job.status}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-6">
            {job.totalTracks > 0 && (
              <dl className="grid grid-cols-3 gap-4">
                <JobStat label="Tracks" value={job.totalTracks} />
                <JobStat label="Clean matches" value={job.matchedTracks} />
                <JobStat label="Processed" value={job.processedTracks} />
              </dl>
            )}

            {job.status === 'Pending' && (
              <div className="space-y-2">
                <Progress value={null} />
                <p className="text-sm text-muted-foreground">
                  Queued — this usually starts within a minute.
                </p>
              </div>
            )}

            {job.status === 'Processing' && (
              <div className="space-y-2">
                <Progress value={progressPercent} />
                <p className="tabular text-sm text-muted-foreground">
                  {job.processedTracks} of {job.totalTracks} tracks processed
                </p>
              </div>
            )}

            {job.status === 'Failed' && (
              <Alert variant="error">
                <AlertTitle>This job failed</AlertTitle>
                <AlertDescription className="space-y-2">
                  <p>
                    {job.errorMessage ||
                      'Something went wrong on our side while processing this playlist.'}
                  </p>
                  <p>
                    Your original playlist is untouched. You can start the job
                    again from the dashboard.
                  </p>
                </AlertDescription>
              </Alert>
            )}

            {job.status === 'Completed' && (
              <div>
                {targetUrl ? (
                  <Button asChild>
                    <a
                      href={targetUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      Open in {providerLabel(job.targetProvider)}
                    </a>
                  </Button>
                ) : (
                  // Library playlists have no public URL — pointing at the app
                  // beats a link that cannot exist.
                  <p className="text-sm text-muted-foreground">
                    Your clean copy is ready in your{' '}
                    {providerLabel(job.targetProvider)} library.
                  </p>
                )}
              </div>
            )}
          </CardContent>
        </Card>

        {job.status === 'Completed' && <SyncSection jobId={jobId} />}

        <TrackMappings userId={initialMe.id} jobId={jobId} job={job} />
      </main>
    </div>
  );
}

function JobStat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </dt>
      <dd className="tabular mt-1 text-xl font-semibold text-foreground">
        {value}
      </dd>
    </div>
  );
}

/**
 * Auto-Sync, stated once and quietly, right after the moment it becomes
 * relevant. The copy promises only what sync does: it adds clean versions of
 * new songs — it never removes anything.
 */
function SyncSection({ jobId }: { jobId: number }) {
  const { data: subscriptionStatus, isLoading: isLoadingSubscription } =
    useSubscriptionStatus();
  const { data: syncConfigs } = useSyncConfigs();
  const enableSyncMutation = useEnableSyncForJob();

  const existingSyncConfig = syncConfigs?.find(
    (config) => config.originalJobId === jobId
  );

  const handleEnableSync = async () => {
    try {
      await enableSyncMutation.mutateAsync(jobId);
      trackAutoSyncEnabled();
      toast.success(
        'Auto-Sync is on — new songs get their clean versions added daily.'
      );
    } catch (error) {
      // 403 = plan limit reached, 400 = subscription required — both carry a
      // human-readable detail from the API.
      if (
        error instanceof ApiError &&
        (error.status === 403 || error.status === 400) &&
        error.detail
      ) {
        toast.error(error.detail);
      } else {
        toast.error('Turning on Auto-Sync failed. Please try again.');
      }
      console.error('Enable sync error:', error);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-display">Keep this copy current</CardTitle>
        <CardDescription>
          Auto-Sync checks the source playlist daily and adds the clean versions
          of new songs to this copy.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {isLoadingSubscription ? (
          <Skeleton className="h-9 w-44" />
        ) : existingSyncConfig ? (
          <>
            <p className="flex items-center gap-2 text-sm font-medium text-foreground">
              <span
                className="size-2 rounded-full bg-success"
                aria-hidden="true"
              />
              Auto-Sync is on
            </p>
            <p className="text-sm text-muted-foreground">
              Runs {existingSyncConfig.syncFrequency.toLowerCase()}.
              {existingSyncConfig.lastSyncedAt && (
                <>
                  {' '}
                  Last synced{' '}
                  <ClientDate date={existingSyncConfig.lastSyncedAt} />.
                </>
              )}
              {existingSyncConfig.nextScheduledSync && (
                <>
                  {' '}
                  Next run{' '}
                  <ClientDate date={existingSyncConfig.nextScheduledSync} />.
                </>
              )}
            </p>
            <Button variant="outline" size="sm" asChild>
              <Link href="/dashboard/sync">Manage Auto-Sync</Link>
            </Button>
          </>
        ) : subscriptionStatus?.hasActiveSubscription ? (
          <Button
            onClick={handleEnableSync}
            disabled={enableSyncMutation.isPending}
          >
            {enableSyncMutation.isPending ? 'Turning on…' : 'Turn on Auto-Sync'}
          </Button>
        ) : (
          <>
            <p className="text-sm text-muted-foreground">
              {CURRENT_PLAN.MARKETING_PRICE}/month for up to{' '}
              {CURRENT_PLAN.FEATURES.MAX_PLAYLISTS} playlists.
            </p>
            <Button variant="outline" asChild>
              <Link href="/subscription">See Auto-Sync</Link>
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
