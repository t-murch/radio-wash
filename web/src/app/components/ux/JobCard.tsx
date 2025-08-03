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
    console.log('[JobCard Debug] SignalR state:', {
      progressState: progressState.status,
      isConnected,
      connectionError,
      useRealtimeProgress:
        job.status === 'Processing' && progressState.status !== 'idle',
    });

  const getStatusStyles = () => {
    switch (job.status) {
      case 'Completed':
        return 'bg-green-100 text-green-800 border-green-300';
      case 'Processing':
        return 'bg-blue-100 text-blue-800 border-blue-300';
      case 'Failed':
        return 'bg-red-100 text-red-800 border-red-300';
      default:
        return 'bg-gray-100 text-gray-800 border-gray-300';
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
      className="block bg-white border border-gray-200 rounded-lg p-4 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="flex justify-between items-start mb-2">
        <div className="w-4/5">
          <h3
            className="font-bold text-gray-800 truncate"
            title={job.targetPlaylistName}
          >
            {job.targetPlaylistName}
          </h3>
          <p
            className="text-sm text-gray-500 truncate"
            title={job.sourcePlaylistName}
          >
            From: {job.sourcePlaylistName}
          </p>
        </div>
        <span
          className={`text-xs font-semibold px-2 py-1 rounded-full border ${getStatusStyles()}`}
        >
          {job.status}
        </span>
      </div>
      {job.status === 'Processing' && (
        <div className="mt-3 space-y-2">
          <div className="flex justify-between text-sm">
            <span className="text-gray-600 truncate">
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
              <span className="text-gray-800 font-medium">
                {displayProgress}%
              </span>
            </div>
          </div>

          {displayMessage && useRealtimeProgress && (
            <div
              className="text-xs text-gray-500 truncate"
              title={displayMessage}
            >
              {displayMessage}
            </div>
          )}

          <div className="w-full bg-gray-200 rounded-full h-2">
            <div
              className={`h-2 rounded-full transition-all duration-500 ease-out ${
                useRealtimeProgress ? 'bg-green-500' : 'bg-blue-500'
              }`}
              style={{ width: `${displayProgress}%` }}
            ></div>
          </div>

          <div className="flex justify-between text-xs text-gray-500">
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
      <p className="text-xs text-gray-400 mt-2 text-right">
        Updated: {new Date(job.updatedAt).toLocaleString()}
      </p>
    </Link>
  );
}
