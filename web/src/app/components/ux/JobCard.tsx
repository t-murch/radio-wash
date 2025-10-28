import { Job } from '@/services/api';
import Link from 'next/link';
import { usePlaylistProgressRealtime } from '@/hooks/usePlaylistProgressRealtime';
import { useAuthToken } from '@/hooks/useAuthToken';

export function JobCard({ job }: { job: Job }) {
  const { authToken } = useAuthToken();

  // Debug logging
  const shouldConnect = job.status === 'Processing';

  const { progressState, isConnected, connectionError } =
    usePlaylistProgressRealtime(
      shouldConnect ? job.id.toString() : null,
      authToken || undefined
    );

  shouldConnect &&
    console.debug('[JobCard Debug] SignalR state:', {
      progressState: progressState.status,
      isConnected,
      connectionError,
      useRealtimeProgress:
        job.status === 'Processing' && progressState.status !== 'idle',
    });

  const getStatusStyles = () => {
    switch (job.status) {
      case 'Completed':
        return 'bg-success-muted text-success border-success/30';
      case 'Processing':
        return 'bg-info-muted text-info border-info/30';
      case 'Failed':
        return 'bg-error-muted text-error border-error/30';
      default:
        return 'bg-muted text-muted-foreground border';
    }
  };

  const getStatusText = () => {
    switch (job.status) {
      case 'Completed':
        return 'Done';
      case 'Processing':
        return 'Active';
      case 'Failed':
        return 'Error';
      default:
        return job.status;
    }
  };

  // Use real-time progress if available, otherwise fall back to job data
  const useRealtimeProgress =
    job.status === 'Processing' && progressState.status !== 'idle';

  const displayProgress = useRealtimeProgress
    ? progressState.progress
    : job.status === 'Completed'
    ? 100
    : job.totalTracks > 0
    ? Math.round((job.processedTracks / job.totalTracks) * 100)
    : 0;

  const displayProcessedTracks = useRealtimeProgress
    ? progressState.processedTracks
    : job.processedTracks;
  const displayTotalTracks = useRealtimeProgress
    ? progressState.totalTracks
    : job.totalTracks;
  const displayBatch = useRealtimeProgress
    ? progressState.currentBatch
    : job.currentBatch;
  const displayMessage = useRealtimeProgress ? progressState.message : null;
  return (
    <Link
      href={`/jobs/${job.id}`}
      className="block bg-card border border rounded-lg p-4 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="flex justify-between items-start mb-2 gap-3">
        <div className="flex-1 min-w-0">
          <h3
            className="font-bold text-foreground truncate"
            title={job.targetPlaylistName}
          >
            {job.targetPlaylistName}
          </h3>
          <p
            className="text-sm text-muted-foreground truncate"
            title={job.sourcePlaylistName}
          >
            From: {job.sourcePlaylistName}
          </p>
        </div>
        <span
          className={`text-xs font-medium px-2.5 py-1 rounded-full border flex-shrink-0 ${getStatusStyles()}`}
          title={job.status}
        >
          {getStatusText()}
        </span>
      </div>
      {job.status === 'Processing' && (
        <div className="mt-3 space-y-2">
          <div className="flex justify-between text-sm">
            <span className="text-muted-foreground truncate">
              {displayBatch || 'Processing...'}
            </span>
            <div className="flex items-center space-x-2">
              {!isConnected && job.status === 'Processing' && (
                <span
                  className="text-xs text-yellow-500"
                  title="Real-time updates unavailable"
                >
                  ⚠️
                </span>
              )}
              <span className="text-foreground font-medium">
                {displayProgress}%
              </span>
            </div>
          </div>

          {displayMessage && useRealtimeProgress && (
            <div
              className="text-xs text-muted-foreground truncate"
              title={displayMessage}
            >
              {displayMessage}
            </div>
          )}

          <div className="w-full bg-muted rounded-full h-2">
            <div
              className={`h-2 rounded-full transition-all duration-500 ease-out ${
                useRealtimeProgress ? 'bg-success' : 'bg-info'
              }`}
              style={{ width: `${displayProgress}%` }}
            ></div>
          </div>

          <div className="flex justify-between text-xs text-muted-foreground">
            <span>
              {displayProcessedTracks} of {displayTotalTracks} tracks
            </span>
            {progressState.estimatedTimeRemaining && useRealtimeProgress && (
              <span className="text-green-600">
                ~{progressState.estimatedTimeRemaining} remaining
              </span>
            )}
          </div>

          {connectionError && (
            <div
              className="text-xs text-red-500 truncate"
              title={connectionError}
            >
              Connection error: {connectionError}
            </div>
          )}
        </div>
      )}
      <p className="text-xs text-muted-foreground mt-2 text-right">
        Updated: {new Date(job.updatedAt).toLocaleString()}
      </p>
    </Link>
  );
}
