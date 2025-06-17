'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../hooks/useAuth';
import { getJobDetails, Job } from '../../services/api';
import { sseService, JobUpdate, TrackProcessed } from '../../services/sse';
import TrackMappings from '@/components/ux/TrackMappings';

function JobDetailsLoading() {
  return (
    <div className="flex justify-center items-center min-h-screen bg-gray-50">
      <div className="text-center">
        <div className="animate-spin rounded-full h-16 w-16 border-t-4 border-b-4 border-green-500"></div>
        <p className="mt-4 text-lg text-gray-600">Loading Job Details...</p>
      </div>
    </div>
  );
}

function JobDetails() {
  const router = useRouter();
  const params = useParams();
  const jobId = parseInt(params.jobId as string);
  const { user } = useAuth();
  const queryClient = useQueryClient();

  const [sseConnected, setSseConnected] = useState(false);
  const [recentTracks, setRecentTracks] = useState<TrackProcessed[]>([]);

  // Fetch job data using React Query
  const {
    data: job,
    isLoading,
    error,
    refetch,
  } = useQuery<Job>({
    queryKey: ['job', user?.id, jobId],
    queryFn: () => getJobDetails(user?.id || 0, jobId),
    enabled: !!user?.id && !isNaN(jobId),
    refetchInterval(query) {
      const jobStatus = query.state.data?.status;
      return jobStatus === 'Processing' ? 5000 : false;
    },
  });

  // Handle real-time job updates
  const handleJobUpdate = useCallback(
    (update: JobUpdate) => {
      if (update.jobId === jobId) {
        queryClient.setQueryData(
          ['job', user?.id, jobId],
          (oldJob: Job | undefined) =>
            oldJob
              ? {
                  ...oldJob,
                  status: update.status,
                  processedTracks: update.processedTracks,
                  totalTracks: update.totalTracks,
                  matchedTracks: update.matchedTracks,
                  errorMessage: update.errorMessage,
                  updatedAt: update.updatedAt,
                }
              : oldJob
        );
      }
    },
    [jobId, queryClient, user?.id]
  );

  // Handle track processed events
  const handleTrackProcessed = useCallback(
    (track: TrackProcessed) => {
      if (track.jobId === jobId) {
        setRecentTracks((prev) => [track, ...prev.slice(0, 4)]);
      }
    },
    [jobId]
  );

  // Connect to SSE for job updates
  useEffect(() => {
    if (!user?.id || !job) return;

    // Set up event listeners
    sseService.onJobUpdate(handleJobUpdate);
    sseService.onTrackProcessed(handleTrackProcessed);

    // Connect to this specific job
    sseService.connect(jobId);
    setSseConnected(true);
    console.log(`Connected to SSE for job ${jobId}`);

    return () => {
      sseService.offJobUpdate(handleJobUpdate);
      sseService.offTrackProcessed(handleTrackProcessed);
      sseService.disconnect(jobId);
      setSseConnected(false);
    };
  }, [user?.id, job, jobId, handleJobUpdate, handleTrackProcessed]);

  const openSpotifyPlaylist = (playlistId: string) => {
    window.open(`https://open.spotify.com/playlist/${playlistId}`, '_blank');
  };

  const refreshJob = () => {
    refetch();
  };

  const getStatusBadgeClass = (status: string) => {
    switch (status) {
      case 'Completed':
        return 'bg-green-100 text-green-800';
      case 'Failed':
        return 'bg-red-100 text-red-800';
      case 'Processing':
        return 'bg-blue-100 text-blue-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const formatDateTime = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto py-4 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <div className="flex items-center space-x-3">
            <h1 className="text-2xl font-bold text-gray-900">RadioWash</h1>
            {/* <div className="flex items-center space-x-1"> */}
            {/*   <div */}
            {/*     className={`w-2 h-2 rounded-full ${ */}
            {/*       sseConnected ? 'bg-green-500' : 'bg-gray-400' */}
            {/*     }`} */}
            {/*     title={ */}
            {/*       sseConnected */}
            {/*         ? 'Real-time updates connected' */}
            {/*         : 'Real-time updates disconnected' */}
            {/*     } */}
            {/*   /> */}
            {/*   <span className="text-xs text-gray-500 hidden sm:inline"> */}
            {/*     {sseConnected ? 'Live' : 'Offline'} */}
            {/*   </span> */}
            {/* </div> */}
          </div>
          {user && (
            <div className="flex items-center space-x-4">
              <button
                onClick={() => router.push('/dashboard')}
                className="text-sm text-gray-500 hover:text-gray-700"
              >
                Back to Dashboard
              </button>
            </div>
          )}
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        {error && (
          <div
            className="p-4 mb-6 text-sm text-red-700 bg-red-100 rounded-lg"
            role="alert"
          >
            Error loading job: {error.message}
          </div>
        )}

        {isLoading ? (
          <div className="flex justify-center items-center h-64">
            <div className="text-center">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-green-500 mx-auto"></div>
              <p className="mt-4 text-gray-500">Loading job data...</p>
            </div>
          </div>
        ) : job ? (
          <div className="space-y-6">
            <div className="bg-white shadow rounded-lg p-6">
              <div className="flex justify-between items-start">
                <div>
                  <h2 className="text-2xl font-semibold text-gray-900">
                    {job.targetPlaylistName}
                  </h2>
                  <p className="text-gray-600">
                    From: {job.sourcePlaylistName}
                  </p>
                </div>
                <div className="flex space-x-2">
                  <button
                    onClick={refreshJob}
                    className="text-sm bg-gray-100 text-gray-800 px-3 py-1 rounded-md hover:bg-gray-200"
                  >
                    Refresh
                  </button>
                </div>
              </div>

              <div className="mt-6 grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="bg-gray-50 p-4 rounded-lg">
                  <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </h3>
                  <div className="mt-2">
                    <span
                      className={`inline-block px-2 py-1 text-sm rounded-full ${getStatusBadgeClass(
                        job.status
                      )}`}
                    >
                      {job.status}
                    </span>
                  </div>
                </div>

                <div className="bg-gray-50 p-4 rounded-lg">
                  <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider">
                    Track Stats
                  </h3>
                  <div className="mt-2">
                    <p className="text-gray-900">
                      {job.matchedTracks} of {job.totalTracks} tracks matched
                    </p>
                    <p className="text-gray-600 text-sm">
                      (
                      {Math.round(
                        (job.matchedTracks / (job.totalTracks || 1)) * 100
                      )}
                      % success rate)
                    </p>
                  </div>
                </div>

                <div className="bg-gray-50 p-4 rounded-lg">
                  <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider">
                    Created
                  </h3>
                  <div className="mt-2">
                    <p className="text-gray-900">
                      {formatDateTime(job.createdAt)}
                    </p>
                    <p className="text-gray-600 text-sm">
                      Last updated: {formatDateTime(job.updatedAt)}
                    </p>
                  </div>
                </div>
              </div>

              {job.status === 'Processing' && (
                <div className="mt-6">
                  <h3 className="text-sm font-medium text-gray-500 mb-2">
                    Progress
                  </h3>
                  <div className="bg-gray-200 rounded-full h-4">
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
                  <p className="text-sm text-gray-500 mt-2">
                    {job.processedTracks} of {job.totalTracks} tracks processed
                  </p>

                  {/* Real-time track processing feed */}
                  {sseConnected && recentTracks.length > 0 && (
                    <div className="mt-4">
                      <h4 className="text-xs font-medium text-gray-500 uppercase tracking-wider mb-2">
                        Currently Processing
                      </h4>
                      <div className="space-y-1">
                        {recentTracks.map((track, index) => (
                          <div
                            key={`${track.sourceTrackName}-${index}`}
                            className="text-sm text-gray-600 animate-fade-in"
                          >
                            {track.hasCleanMatch ? '✓' : '✗'}{' '}
                            {track.sourceTrackName} - {track.sourceArtistName}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {job.status === 'Failed' && (
                <div className="mt-6">
                  <div className="p-4 bg-red-50 text-red-700 rounded-lg">
                    <h3 className="text-sm font-medium mb-2">Error Message</h3>
                    <p>
                      {job.errorMessage ||
                        'An unknown error occurred while processing this job.'}
                    </p>
                  </div>
                </div>
              )}

              {job.status === 'Completed' && job.targetPlaylistId && (
                <div className="mt-6">
                  <button
                    onClick={() => openSpotifyPlaylist(job.targetPlaylistId!)}
                    className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
                  >
                    Open Playlist in Spotify
                  </button>
                </div>
              )}
            </div>

            {/* Track Mappings - TODO: Implement TrackMappings component */}
            {user && job && <TrackMappings userId={user.id} jobId={job.id} />}
          </div>
        ) : (
          <div className="text-center p-8">
            <p className="text-gray-500">
              Job not found or you don&apos;t have access to it.
            </p>
            <button
              onClick={() => router.push('/dashboard')}
              className="mt-4 inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
            >
              Back to Dashboard
            </button>
          </div>
        )}
      </main>
    </div>
  );
}

export default function JobDetailsPage() {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!isLoading && !isAuthenticated) {
      router.replace('/auth');
    }
  }, [isLoading, isAuthenticated, router]);

  if (isLoading || !isAuthenticated) {
    return <JobDetailsLoading />;
  }

  return <JobDetails />;
}
