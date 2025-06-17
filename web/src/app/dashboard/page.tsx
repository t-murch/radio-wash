'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { Suspense, useCallback, useEffect, useState } from 'react';
import { useAuth } from '../hooks/useAuth';
import {
  createCleanPlaylistJob,
  getUserJobs,
  getUserPlaylists,
  Job,
  Playlist,
} from '../services/api';
import { JobUpdate, sseService } from '../services/sse';
import { JobCard } from '@/components/ux/JobCard';

function DashboardLoading() {
  return (
    <div className="flex justify-center items-center min-h-screen bg-gray-50">
      <div className="text-center">
        <div className="animate-spin rounded-full h-16 w-16 border-t-4 border-b-4 border-green-500"></div>
        <p className="mt-4 text-lg text-gray-600">Loading Dashboard...</p>
      </div>
    </div>
  );
}

function Dashboard() {
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();
  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const [customName, setCustomName] = useState('');

  const openSpotifyPlaylist = (playlistId: string) => {
    window.open(`https://open.spotify.com/playlist/${playlistId}`, '_blank');
  };

  const { data: playlists = [] } = useQuery<Playlist[]>({
    queryKey: ['playlists', user?.id],
    queryFn: () => getUserPlaylists(user?.id || 0),
    enabled: !!user?.id,
  });
  const { data: jobs = [] } = useQuery<Job[]>({
    queryKey: ['jobs', user?.id],
    queryFn: () => getUserJobs(user?.id || 0),
    enabled: !!user?.id,
  });

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName: string }) =>
      createCleanPlaylistJob(
        user?.id || 0,
        vars.sourcePlaylistId,
        vars.targetName
      ),
    onSuccess: async (newJob) => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] });
      setSelectedPlaylistId('');
      setCustomName('');

      // Connect to SSE for the new job
      sseService.connect(newJob.id);
      console.log(`Connected to SSE for job ${newJob.id}`);
    },
  });

  const handleJobUpdate = useCallback(
    (update: JobUpdate) => {
      queryClient.setQueryData<Job[]>(['jobs'], (oldJobs = []) =>
        oldJobs.map((j) =>
          j.id === update.jobId
            ? { ...j, ...update, status: update.status.toString() }
            : j
        )
      );
    },
    [queryClient]
  );

  // Set up SSE listeners and connect to active jobs
  useEffect(() => {
    if (!user?.id) return;

    // Set up event listeners
    sseService.onJobUpdate(handleJobUpdate);

    // Connect to active jobs
    const activeJobs = jobs.filter(
      (job) => job.status === 'Processing' || job.status === 'Pending'
    );

    activeJobs.forEach((job) => {
      sseService.connect(job.id);
      console.log(`Connected to SSE for existing job ${job.id}`);
    });

    return () => {
      sseService.offJobUpdate(handleJobUpdate);
      sseService.disconnectAll();
    };
  }, [user?.id, handleJobUpdate, jobs]);

  const handleCreatePlaylist = () => {
    const selected = playlists.find((p) => p.id === selectedPlaylistId);
    if (!selected) return;
    const targetName = customName.trim() || `Clean - ${selected.name}`;
    createJobMutation.mutate({ sourcePlaylistId: selected.id, targetName });
  };

  return (
    <div className="min-h-screen bg-gray-100">
      <header className="bg-white shadow-sm sticky top-0 z-10">
        <div className="max-w-7xl mx-auto py-3 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <div className="flex items-center space-x-3">
            <h1 className="text-2xl font-bold text-gray-800">RadioWash</h1>
            <div className="flex items-center space-x-1"></div>
          </div>
          {user && (
            <div className="flex items-center space-x-4">
              <span className="text-gray-600 hidden sm:inline">
                Welcome, {user.displayName}
              </span>
              <Image
                src={user.profileImageUrl || `/user.svg`}
                alt="User Profile"
                className="rounded-full"
                width={40}
                height={40}
                priority
              />
              <button
                onClick={logout}
                className="text-sm font-medium text-gray-500 hover:text-gray-700"
              >
                Logout
              </button>
            </div>
          )}
        </div>
      </header>

      <main className="max-w-7xl mx-auto py-8 px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-5 gap-8">
          <div className="lg:col-span-3 space-y-8">
            <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-gray-900 mb-4">
                Create a Clean Playlist
              </h2>
              <div className="space-y-4">
                <select
                  value={selectedPlaylistId}
                  onChange={(e) => setSelectedPlaylistId(e.target.value)}
                  className="block w-full p-3 border border-gray-300 rounded-md"
                >
                  <option value="">-- Choose a playlist --</option>
                  {playlists.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({p.trackCount} tracks)
                    </option>
                  ))}
                </select>
                <input
                  type="text"
                  placeholder="New Playlist Name (Optional)"
                  value={customName}
                  onChange={(e) => setCustomName(e.target.value)}
                  className="block w-full p-3 border border-gray-300 rounded-md"
                />
                <button
                  onClick={handleCreatePlaylist}
                  disabled={!selectedPlaylistId || createJobMutation.isPending}
                  className="w-full bg-green-600 text-white py-3 rounded-md hover:bg-green-700 disabled:opacity-50"
                >
                  {createJobMutation.isPending
                    ? 'Working on it...'
                    : 'Create Clean Version'}
                </button>
              </div>
            </div>

            <div className="space-y-4">
              {playlists.length === 0 ? (
                <p className="text-gray-500">
                  No playlists found. Make sure you have playlists on Spotify.
                </p>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                  {playlists.map((playlist, idx) => (
                    <div
                      key={idx}
                      className="border rounded-lg p-4 bg-white shadow-sm hover:shadow-md transition-shadow cursor-pointer"
                      onClick={() => setSelectedPlaylistId(playlist.id)}
                    >
                      <div className="aspect-square w-full bg-gray-200 rounded-md mb-2 overflow-hidden">
                        {playlist.imageUrl ? (
                          <Image
                            src={playlist.imageUrl}
                            alt={playlist.name}
                            // className="w-full h-full object-cover"
                            className="object-cover"
                            width={200}
                            height={200}
                            priority={idx < 7}
                          />
                        ) : (
                          <div className="w-full h-full flex items-center justify-center">
                            <span className="text-gray-400">No Image</span>
                          </div>
                        )}
                      </div>
                      <h3 className="font-semibold text-gray-900 truncate">
                        {playlist.name}
                      </h3>
                      <p className="text-sm text-gray-500">
                        {playlist.trackCount} tracks
                      </p>
                      <div className="flex justify-between mt-2">
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            setSelectedPlaylistId(playlist.id);
                          }}
                          className="text-xs bg-green-100 text-green-800 px-2 py-1 rounded-md hover:bg-green-200"
                        >
                          Make Clean
                        </button>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            openSpotifyPlaylist(playlist.id);
                          }}
                          className="text-xs bg-gray-100 text-gray-800 px-2 py-1 rounded-md hover:bg-gray-200"
                        >
                          Open in Spotify
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
          <div className="lg:col-span-2">
            <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
              <h2 className="text-xl font-semibold text-gray-900 mb-4">
                Job Status
              </h2>
              <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-2">
                {jobs.length > 0 ? (
                  jobs.map((job) => <JobCard key={job.id} job={job} />)
                ) : (
                  <p className="text-gray-500 text-center py-4">No jobs yet.</p>
                )}
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}

export default function DashboardPage() {
  const { isAuthenticated, isLoading } = useAuth();
  const router = useRouter();

  useEffect(() => {
    // If the auth state is resolved and the user is not authenticated,
    // redirect them to the login page.
    if (!isLoading && !isAuthenticated) {
      router.replace('/auth');
    }
  }, [isLoading, isAuthenticated, router]);

  // While loading or if not authenticated (before redirect), show a loading screen.
  if (isLoading || !isAuthenticated) {
    return <DashboardLoading />;
  }

  // Once authenticated, render the dashboard.
  return (
    <Suspense fallback={<DashboardLoading />}>
      <Dashboard />
    </Suspense>
  );
}
