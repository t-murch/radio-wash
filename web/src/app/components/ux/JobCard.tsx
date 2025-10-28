import { Job } from '@/services/api';
import Link from 'next/link';
import { usePlaylistProgressRealtime } from '@/hooks/usePlaylistProgressRealtime';
import { useAuthToken } from '@/hooks/useAuthToken';
import { logger } from '@/lib/logger';
import { useEffect, useRef } from 'react';

export function JobCard({ job }: { job: Job }) {
  const { authToken } = useAuthToken();
  const prevStateRef = useRef<{
    jobStatus: string;
    isConnected: boolean;
    connectionError?: string;
    progressStatus: string;
  }>({
    jobStatus: job.status,
    isConnected: false,
    connectionError: undefined,
    progressStatus: 'idle',
  });

  const shouldConnect = job.status === 'Processing';

  const { progressState, isConnected, connectionError } =
    usePlaylistProgressRealtime(
      shouldConnect ? job.id.toString() : null,
      authToken || undefined
    );

  // Only log meaningful state changes to prevent spam
  useEffect(() => {
    if (!shouldConnect) return;

    const prevState = prevStateRef.current;
    const currentState = {
      jobStatus: job.status,
      isConnected,
      connectionError,
      progressStatus: progressState.status,
    };

    // Log job status changes
    if (prevState.jobStatus !== currentState.jobStatus) {
      logger.debug('[JobCard] Job status changed', {
        jobId: job.id,
        from: prevState.jobStatus,
        to: currentState.jobStatus,
        shouldConnect,
      });
    }

    // Log SignalR connection state changes (only for processing jobs)
    if (shouldConnect && prevState.isConnected !== currentState.isConnected) {
      logger.debug('[JobCard] SignalR connection state changed', {
        jobId: job.id,
        connected: currentState.isConnected,
        progressStatus: currentState.progressStatus,
      });
    }

    // Log connection errors when they first appear
    if (
      shouldConnect &&
      !prevState.connectionError &&
      currentState.connectionError
    ) {
      logger.warn('[JobCard] SignalR connection error occurred', {
        jobId: job.id,
        connectionError: currentState.connectionError,
        isConnected: currentState.isConnected,
      });
    }

    // Log progress status changes (connecting -> connected -> processing)
    if (
      shouldConnect &&
      prevState.progressStatus !== currentState.progressStatus
    ) {
      logger.debug('[JobCard] Progress status changed', {
        jobId: job.id,
        from: prevState.progressStatus,
        to: currentState.progressStatus,
        isConnected: currentState.isConnected,
      });
    }

    // Update previous state reference
    prevStateRef.current = currentState;
  }, [
    job.id,
    job.status,
    shouldConnect,
    isConnected,
    connectionError,
    progressState.status,
  ]);

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
      className="block bg-card border border-border rounded-lg p-4 shadow-sm hover:shadow-md transition-shadow"
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
                  className="text-xs text-warning"
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
              <span className="text-success">
                ~{progressState.estimatedTimeRemaining} remaining
              </span>
            )}
          </div>

          {connectionError && (
            <div
              className="text-xs text-error truncate"
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
