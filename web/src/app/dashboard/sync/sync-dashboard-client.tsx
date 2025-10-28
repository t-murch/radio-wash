'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { GlobalHeader } from '@/components/GlobalHeader';
import { Button } from '@/components/ui/button';
import { 
  useSubscriptionStatus, 
  useSyncConfigs, 
  useSubscribeToSync 
} from '@/hooks/useSubscriptionSync';
import { 
  triggerManualSync, 
  disableSync, 
  updateSyncFrequency,
  type User,
  type PlaylistSyncConfig,
  type SyncResult 
} from '../../services/api';
import { toast } from 'sonner';
import { useMutation, useQueryClient } from '@tanstack/react-query';

// Helper functions
const formatDateTime = (dateString: string) => {
  return new Date(dateString).toLocaleString();
};

const getStatusIndicator = (status?: string) => {
  switch (status) {
    case 'completed':
      return <div className="w-2 h-2 bg-success rounded-full" />;
    case 'failed':
      return <div className="w-2 h-2 bg-error rounded-full" />;
    case 'running':
      return <div className="w-2 h-2 bg-info rounded-full animate-pulse" />;
    default:
      return <div className="w-2 h-2 bg-muted rounded-full" />;
  }
};

interface SyncConfigCardProps {
  config: PlaylistSyncConfig;
  onManualSync: (configId: number) => void;
  onDisable: (configId: number) => void;
  onUpdateFrequency: (configId: number, frequency: string) => void;
  isProcessing: boolean;
}

function SyncConfigCard({ 
  config, 
  onManualSync, 
  onDisable, 
  onUpdateFrequency, 
  isProcessing 
}: SyncConfigCardProps) {
  const router = useRouter();

  return (
    <div className="border border-border rounded-lg p-4 bg-card">
      <div className="flex justify-between items-start mb-3">
        <div>
          <h3 className="font-medium text-foreground">{config.targetPlaylistName}</h3>
          <p className="text-sm text-muted-foreground">From: {config.sourcePlaylistName}</p>
        </div>
        <div className="flex items-center space-x-2">
          {getStatusIndicator(config.lastSyncStatus)}
          <span className="text-xs text-muted-foreground">
            {config.isActive ? 'Active' : 'Disabled'}
          </span>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 text-sm mb-4">
        <div>
          <span className="text-muted-foreground">Frequency:</span>
          <p className="font-medium">{config.syncFrequency}</p>
        </div>
        <div>
          <span className="text-muted-foreground">Last Sync:</span>
          <p className="font-medium">
            {config.lastSyncedAt ? formatDateTime(config.lastSyncedAt) : 'Never'}
          </p>
        </div>
        <div>
          <span className="text-muted-foreground">Next Sync:</span>
          <p className="font-medium">
            {config.nextScheduledSync ? formatDateTime(config.nextScheduledSync) : 'Not scheduled'}
          </p>
        </div>
        <div>
          <span className="text-muted-foreground">Status:</span>
          <p className="font-medium">
            {config.lastSyncStatus || 'Pending'}
            {config.lastSyncError && (
              <span className="text-error block text-xs truncate" title={config.lastSyncError}>
                Error: {config.lastSyncError}
              </span>
            )}
          </p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={() => onManualSync(config.id)}
          disabled={isProcessing || !config.isActive}
        >
          Manual Sync
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => router.push(`/jobs/${config.originalJobId}`)}
        >
          View Job
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => onDisable(config.id)}
          disabled={isProcessing}
          className="text-red-600 hover:text-red-700"
        >
          Disable
        </Button>
      </div>
    </div>
  );
}

export function SyncDashboardClient({ initialUser }: { initialUser: User }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [processingConfigId, setProcessingConfigId] = useState<number | null>(null);

  const { data: subscriptionStatus, isLoading: isLoadingSubscription } = useSubscriptionStatus();
  const { data: syncConfigs, isLoading: isLoadingSyncConfigs } = useSyncConfigs();
  const subscribeToSyncMutation = useSubscribeToSync();

  const manualSyncMutation = useMutation<SyncResult, Error, number>({
    mutationFn: triggerManualSync,
    onSuccess: (result, configId) => {
      if (result.success) {
        toast.success(`Sync completed! Added ${result.tracksAdded}, removed ${result.tracksRemoved} tracks.`);
      } else {
        toast.error(`Sync failed: ${result.errorMessage}`);
      }
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
      setProcessingConfigId(null);
    },
    onError: (error) => {
      toast.error('Manual sync failed. Please try again.');
      console.error('Manual sync error:', error);
      setProcessingConfigId(null);
    },
  });

  const disableSyncMutation = useMutation<{ success: boolean }, Error, number>({
    mutationFn: disableSync,
    onSuccess: () => {
      toast.success('Sync disabled successfully');
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
    },
    onError: (error) => {
      toast.error('Failed to disable sync');
      console.error('Disable sync error:', error);
    },
  });

  const handleManualSync = async (configId: number) => {
    setProcessingConfigId(configId);
    await manualSyncMutation.mutateAsync(configId);
  };

  const handleDisableSync = async (configId: number) => {
    if (confirm('Are you sure you want to disable sync for this playlist? You can re-enable it from the job details page.')) {
      await disableSyncMutation.mutateAsync(configId);
    }
  };

  const handleUpdateFrequency = async (configId: number, frequency: string) => {
    // This would require implementing the mutation
    toast.info('Frequency update coming soon!');
  };

  const handleSubscribeToSync = () => {
    // Navigate to subscription page to show full value proposition first
    router.push('/subscription');
  };

  if (isLoadingSubscription || isLoadingSyncConfigs) {
    return (
      <div className="min-h-screen bg-background">
        <GlobalHeader
          user={initialUser}
          showBackButton={true}
          backButtonHref="/dashboard"
          backButtonLabel="Back to Dashboard"
        />
        <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="text-center">Loading sync dashboard...</div>
        </main>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <GlobalHeader
        user={initialUser}
        showBackButton={true}
        backButtonHref="/dashboard"
        backButtonLabel="Back to Dashboard"
      />
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <div className="mb-6">
          <h1 className="text-3xl font-bold text-foreground">Playlist Sync Management</h1>
          <p className="text-muted-foreground mt-2">
            Manage automatic synchronization for your clean playlists
          </p>
        </div>

        {!subscriptionStatus?.hasActiveSubscription ? (
          <div className="bg-card border border-border rounded-lg p-6 text-center">
            <h2 className="text-xl font-semibold text-foreground mb-2">
              Subscribe to Enable Sync
            </h2>
            <p className="text-muted-foreground mb-4">
              Automatically keep your clean playlists synchronized with source playlists. 
              New tracks are cleaned and added daily.
            </p>
            <Button
              onClick={handleSubscribeToSync}
              className="bg-brand hover:bg-brand-hover text-brand-foreground"
            >
              Learn More & Subscribe
            </Button>
          </div>
        ) : !syncConfigs?.length ? (
          <div className="bg-card border border-border rounded-lg p-6 text-center">
            <h2 className="text-xl font-semibold text-foreground mb-2">
              No Sync Configurations
            </h2>
            <p className="text-muted-foreground mb-4">
              You haven't enabled sync for any playlists yet. Complete a playlist cleaning job 
              and enable sync from the job details page.
            </p>
            <Button
              variant="outline"
              onClick={() => router.push('/dashboard')}
            >
              Go to Dashboard
            </Button>
          </div>
        ) : (
          <div>
            <div className="flex justify-between items-center mb-4">
              <div>
                <h2 className="text-xl font-semibold text-foreground">
                  Active Sync Configurations ({syncConfigs.length})
                </h2>
                <div className="text-sm text-muted-foreground">
                  Subscription Status: 
                  <span className="text-green-600 font-medium ml-1">Active</span>
                </div>
              </div>
              <Button
                variant="outline"
                onClick={() => router.push('/subscription')}
              >
                Manage Billing
              </Button>
            </div>

            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {syncConfigs.map((config) => (
                <SyncConfigCard
                  key={config.id}
                  config={config}
                  onManualSync={handleManualSync}
                  onDisable={handleDisableSync}
                  onUpdateFrequency={handleUpdateFrequency}
                  isProcessing={processingConfigId === config.id || manualSyncMutation.isPending}
                />
              ))}
            </div>
          </div>
        )}
      </main>
    </div>
  );
}