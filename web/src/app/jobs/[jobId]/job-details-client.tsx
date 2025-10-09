'use client';

import { useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { getJobDetails, Job, User } from '../../services/api';
import TrackMappings from '@/components/ux/TrackMappings';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { useSubscriptionStatus, useEnableSyncForJob, useSubscribeToSync, useSyncConfigs } from '@/hooks/useSubscriptionSync';
import { useState } from 'react';
import { toast } from 'sonner';

// Helper functions for UI, can be moved to a utils file
const getStatusBadgeClass = (status: string) => {
  switch (status) {
    case 'Completed':
      return 'bg-green-100 text-green-800';
    case 'Failed':
      return 'bg-red-100 text-red-800';
    case 'Processing':
      return 'bg-blue-100 text-blue-800';
    default:
      return 'bg-muted text-muted-foreground';
  }
};

const formatDateTime = (dateString: string) => {
  return new Date(dateString).toLocaleString();
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
  const router = useRouter();
  const [isEnablingSync, setIsEnablingSync] = useState(false);

  // Use React Query to manage the job data, with initial data from the server
  const { data: job, isLoading } = useQuery<Job>({
    queryKey: ['job', initialMe?.id, jobId],
    queryFn: () => getJobDetails(initialMe!.id, jobId),
    initialData: initialJob,
    enabled: !!initialMe,
  });

  // Subscription and sync hooks
  const { data: subscriptionStatus, isLoading: isLoadingSubscription } = useSubscriptionStatus();
  const { data: syncConfigs } = useSyncConfigs();
  const enableSyncMutation = useEnableSyncForJob();
  const subscribeToSyncMutation = useSubscribeToSync();

  // Check if sync is already enabled for this job
  const existingSyncConfig = syncConfigs?.find(config => config.originalJobId === jobId);

  const handleEnableSync = async () => {
    if (!subscriptionStatus?.hasActiveSubscription) {
      toast.error('Please subscribe to enable sync functionality');
      return;
    }

    setIsEnablingSync(true);
    try {
      await enableSyncMutation.mutateAsync(jobId);
      toast.success('Sync enabled successfully! Your playlist will be synchronized daily.');
    } catch (error) {
      toast.error('Failed to enable sync. Please try again.');
      console.error('Enable sync error:', error);
    } finally {
      setIsEnablingSync(false);
    }
  };

  const handleSubscribeToSync = async () => {
    try {
      await subscribeToSyncMutation.mutateAsync();
      // Note: The mutation will redirect to Stripe checkout on success
    } catch (error) {
      if (error instanceof Error && error.message.includes('404')) {
        toast.error('Subscription service not yet configured. Please contact support.');
      } else {
        toast.error('Subscription failed. Please try again.');
      }
      console.error('Subscribe error:', error);
    }
  };

  if (isLoading || !job) {
    return <div>Loading job details...</div>;
  }

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialMe}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8 space-y-6">
        <div className="bg-card shadow rounded-lg p-6">
          <div className="flex justify-between items-start">
            <div>
              <h2 className="text-2xl font-semibold text-foreground">
                {job.targetPlaylistName}
              </h2>
              <p className="text-muted-foreground">
                From: {job.sourcePlaylistName}
              </p>
            </div>
            <span
              className={`inline-block px-2 py-1 text-sm rounded-full ${getStatusBadgeClass(
                job.status
              )}`}
            >
              {job.status}
            </span>
          </div>

          <div className="mt-6 grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* ... UI for stats, progress bar, etc. ... */}
          </div>

          {job.status === 'Processing' && (
            <div className="mt-6">
              <h3 className="text-sm font-medium text-muted-foreground mb-2">
                Progress
              </h3>
              <div className="bg-muted rounded-full h-4">
                <div
                  className="bg-blue-600 h-4 rounded-full transition-all duration-500"
                  style={{
                    width: `${
                      job.totalTracks > 0
                        ? (job.processedTracks / job.totalTracks) * 100
                        : 0
                    }%`,
                  }}
                ></div>
              </div>
              <p className="text-sm text-muted-foreground mt-2">
                {job.processedTracks} of {job.totalTracks} tracks processed
              </p>
            </div>
          )}

          {job.status === 'Completed' && job.targetPlaylistId && (
            <div className="mt-6 space-y-4">
              <a
                href={`https://open.spotify.com/playlist/${job.targetPlaylistId}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-primary-foreground bg-green-600 hover:bg-green-700"
              >
                Open Playlist in Spotify
              </a>

              {/* Sync Management Section */}
              <div className="border border-border rounded-lg p-6 bg-card">
                <div className="mb-6">
                  <h3 className="text-xl font-semibold text-foreground mb-3">
                    🔄 Never Manually Sync Again
                  </h3>
                  <p className="text-muted-foreground mb-4">
                    Your playlist just took time to process. What if your source playlist changes tomorrow? 
                    With Auto-Sync, your clean playlist stays updated automatically - no more waiting or manual work.
                  </p>
                  
                  <div className="grid md:grid-cols-2 gap-4 mb-4">
                    <div className="bg-red-50 dark:bg-red-950/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
                      <h4 className="font-medium text-red-800 dark:text-red-200 mb-2">❌ Manual Way</h4>
                      <ul className="text-sm text-red-700 dark:text-red-300 space-y-1">
                        <li>• Check source playlist for changes</li>
                        <li>• Create new cleaning job</li>
                        <li>• Wait for processing</li>
                        <li>• Replace old playlist</li>
                        <li>• <strong>Repeat every time it changes</strong></li>
                      </ul>
                    </div>
                    
                    <div className="bg-green-50 dark:bg-green-950/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
                      <h4 className="font-medium text-green-800 dark:text-green-200 mb-2">✅ Auto-Sync Way</h4>
                      <ul className="text-sm text-green-700 dark:text-green-300 space-y-1">
                        <li>• Enable sync once</li>
                        <li>• System checks daily at midnight</li>
                        <li>• New tracks cleaned automatically</li>
                        <li>• Your playlist stays current</li>
                        <li>• <strong>Set it and forget it</strong></li>
                      </ul>
                    </div>
                  </div>

                  <div className="bg-blue-50 dark:bg-blue-950/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
                    <div className="flex items-start space-x-3">
                      <div className="flex-shrink-0">
                        <div className="w-8 h-8 bg-blue-100 dark:bg-blue-900 rounded-full flex items-center justify-center">
                          <span className="text-blue-600 dark:text-blue-400 text-sm font-bold">$5</span>
                        </div>
                      </div>
                      <div>
                        <h4 className="font-medium text-blue-800 dark:text-blue-200 mb-1">
                          $5/month • Up to 10 playlists • 200 tracks each
                        </h4>
                        <p className="text-sm text-blue-700 dark:text-blue-300">
                          Save hours of manual work. Your time is worth more than $5.
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                {isLoadingSubscription ? (
                  <div className="text-sm text-muted-foreground">Loading subscription status...</div>
                ) : existingSyncConfig ? (
                  <div className="space-y-2">
                    <div className="flex items-center space-x-2">
                      <div className="w-2 h-2 bg-green-500 rounded-full"></div>
                      <span className="text-sm font-medium text-foreground">
                        Sync Enabled
                      </span>
                    </div>
                    <p className="text-sm text-muted-foreground">
                      Frequency: {existingSyncConfig.syncFrequency}
                      {existingSyncConfig.lastSyncedAt && (
                        <span className="block">
                          Last synced: {formatDateTime(existingSyncConfig.lastSyncedAt)}
                        </span>
                      )}
                      {existingSyncConfig.nextScheduledSync && (
                        <span className="block">
                          Next sync: {formatDateTime(existingSyncConfig.nextScheduledSync)}
                        </span>
                      )}
                    </p>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => router.push('/dashboard/sync')}
                    >
                      Manage Sync Settings
                    </Button>
                  </div>
                ) : subscriptionStatus?.hasActiveSubscription ? (
                  <Button
                    onClick={handleEnableSync}
                    disabled={isEnablingSync || enableSyncMutation.isPending}
                    className="bg-blue-600 hover:bg-blue-700 text-white"
                  >
                    {isEnablingSync || enableSyncMutation.isPending ? 'Enabling...' : 'Enable Sync'}
                  </Button>
                ) : (
                  <div className="space-y-3">
                    <div className="text-sm text-muted-foreground">
                      Subscribe to enable automatic playlist synchronization
                    </div>
                    <Button
                      onClick={handleSubscribeToSync}
                      disabled={subscribeToSyncMutation.isPending}
                      className="bg-purple-600 hover:bg-purple-700 text-white"
                    >
                      {subscribeToSyncMutation.isPending ? 'Subscribing...' : 'Subscribe to Sync'}
                    </Button>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        {initialMe && <TrackMappings userId={initialMe.id} jobId={jobId} />}
      </main>
    </div>
  );
}
