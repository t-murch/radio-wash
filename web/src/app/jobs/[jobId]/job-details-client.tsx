'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { createClient } from '@/lib/supabase/client';
import { getJobDetails, Job, User } from '../../services/api';
import TrackMappings from '@/components/ux/TrackMappings'; // Assuming this component exists

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
      return 'bg-gray-100 text-gray-800';
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
  const queryClient = useQueryClient();
  const router = useRouter();
  const supabase = createClient();

  // Use React Query to manage the job data, with initial data from the server
  const { data: job, isLoading } = useQuery<Job>({
    queryKey: ['job', initialMe?.id, jobId],
    queryFn: () => getJobDetails(initialMe!.id, jobId),
    initialData: initialJob,
    enabled: !!initialMe,
  });

  // Set up Supabase Realtime subscription for this specific job
  useEffect(() => {
    if (!initialMe) return;

    const channel = supabase
      .channel(`job-details-${jobId}`)
      .on(
        'postgres_changes',
        {
          event: '*',
          schema: 'public',
          table: 'CleanPlaylistJobs',
          filter: `id=eq.${jobId}`,
        },
        (payload) => {
          // When an update comes in, invalidate the query to refetch the latest data
          queryClient.invalidateQueries({
            queryKey: ['job', initialMe.id, jobId],
          });
        }
      )
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, [supabase, jobId, queryClient, initialMe]);

  if (isLoading || !job) {
    return <div>Loading job details...</div>;
  }

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto py-4 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <h1 className="text-2xl font-bold text-gray-900">Job Details</h1>
          <button
            onClick={() => router.push('/dashboard')}
            className="text-sm font-medium text-gray-500 hover:text-gray-700"
          >
            Back to Dashboard
          </button>
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8 space-y-6">
        <div className="bg-white shadow rounded-lg p-6">
          <div className="flex justify-between items-start">
            <div>
              <h2 className="text-2xl font-semibold text-gray-900">
                {job.targetPlaylistName}
              </h2>
              <p className="text-gray-600">From: {job.sourcePlaylistName}</p>
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
            </div>
          )}

          {job.status === 'Completed' && job.targetPlaylistId && (
            <div className="mt-6">
              <a
                href={`https://open.spotify.com/track/$${job.targetPlaylistId}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-green-600 hover:bg-green-700"
              >
                Open Playlist in Spotify
              </a>
            </div>
          )}
        </div>

        {initialMe && <TrackMappings userId={initialMe.id} jobId={jobId} />}
      </main>
    </div>
  );
}
