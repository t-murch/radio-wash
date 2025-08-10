'use client';

import { useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { getJobDetails, Job, User } from '../../services/api';
import TrackMappings from '@/components/ux/TrackMappings';
import { GlobalHeader } from '@/components/GlobalHeader';

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

  // Use React Query to manage the job data, with initial data from the server
  const { data: job, isLoading } = useQuery<Job>({
    queryKey: ['job', initialMe?.id, jobId],
    queryFn: () => getJobDetails(initialMe!.id, jobId),
    initialData: initialJob,
    enabled: !!initialMe,
  });

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
            <div className="mt-6">
              <a
                href={`https://open.spotify.com/playlist/${job.targetPlaylistId}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-primary-foreground bg-green-600 hover:bg-green-700"
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
