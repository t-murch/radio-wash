'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Image from 'next/image';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { createClient } from '@/lib/supabase/client';
import {
  createCleanPlaylistJob,
  getUserJobs,
  getUserPlaylists,
  getMe,
  Job,
  Playlist,
  User,
} from '../services/api';
import { JobCard } from '@/components/ux/JobCard';
import type { User as SupabaseUser } from '@supabase/supabase-js';

export function DashboardClient({
  serverUser,
  initialMe,
  initialPlaylists,
  initialJobs,
}: {
  serverUser: SupabaseUser;
  initialMe: User;
  initialPlaylists: Playlist[];
  initialJobs: Job[];
}) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const supabase = createClient();

  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const [customName, setCustomName] = useState('');

  // Use React Query to manage data, with initial data from the server
  const { data: me } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    initialData: initialMe,
  });

  const { data: playlists = [] } = useQuery<Playlist[]>({
    queryKey: ['playlists', me?.id],
    queryFn: () => getUserPlaylists(me!.id),
    enabled: !!me,
    initialData: initialPlaylists,
  });

  const { data: jobs = [] } = useQuery<Job[]>({
    queryKey: ['jobs', me?.id],
    queryFn: () => getUserJobs(me!.id),
    enabled: !!me,
    initialData: initialJobs,
  });

  const createJobMutation = useMutation({
    mutationFn: (vars: { sourcePlaylistId: string; targetName: string }) =>
      createCleanPlaylistJob(me!.id, vars.sourcePlaylistId, vars.targetName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['jobs'] });
      setSelectedPlaylistId('');
      setCustomName('');
    },
  });

  // Set up Supabase Realtime subscription for job updates
  useEffect(() => {
    if (!me) return;

    const channel = supabase
      .channel('db-jobs')
      .on(
        'postgres_changes',
        {
          event: '*',
          schema: 'public',
          table: 'CleanPlaylistJobs',
          filter: `user_id=eq.${me.id}`,
        },
        (payload) => {
          queryClient.invalidateQueries({ queryKey: ['jobs', me.id] });
        }
      )
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, [supabase, queryClient, me]);

  const handleCreatePlaylist = () => {
    const selected = playlists.find((p) => p.id === selectedPlaylistId);
    if (!selected || !me) return;
    const targetName = customName.trim() || `Clean - ${selected.name}`;
    createJobMutation.mutate({ sourcePlaylistId: selected.id, targetName });
  };

  const handleLogout = async () => {
    await supabase.auth.signOut();
    router.refresh(); // Refresh the page to trigger redirect
  };

  return (
    <div className="min-h-screen bg-gray-100">
      <header className="bg-white shadow-sm sticky top-0 z-10">
        <div className="max-w-7xl mx-auto py-3 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
          <h1 className="text-2xl font-bold text-gray-800">RadioWash</h1>
          <div className="flex items-center space-x-4">
            <span className="text-gray-600 hidden sm:inline">
              Welcome, {serverUser.user_metadata.name}
            </span>
            <Image
              src={serverUser.user_metadata.avatar_url || `/user.svg`}
              alt="User Profile"
              className="rounded-full"
              width={40}
              height={40}
              priority
            />
            <button
              onClick={handleLogout}
              className="text-sm font-medium text-gray-500 hover:text-gray-700"
            >
              Logout
            </button>
          </div>
        </div>
      </header>
      <main className="max-w-7xl mx-auto py-8 px-4 sm:px-6 lg:px-8">
        {/* The rest of your dashboard UI goes here, using the data from React Query */}
        {/* ... (select playlist, create job button, job list, etc.) ... */}
      </main>
    </div>
  );
}
