import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach } from 'vitest';

import { JobDetailsClient } from '../job-details-client';
import { ApiError, getJobDetails, type Job, type User } from '@/services/api';
import {
  useEnableSyncForJob,
  useSubscriptionStatus,
  useSyncConfigs,
} from '@/hooks/useSubscriptionSync';
import { toast } from 'sonner';

vi.mock('@/services/api', () => ({
  ApiError: class ApiError extends Error {
    constructor(
      public readonly status: number,
      message: string,
      public readonly detail?: string,
      public readonly problemType?: string
    ) {
      super(message);
      this.name = 'ApiError';
    }
  },
  getJobDetails: vi.fn(),
}));

vi.mock('@/hooks/useSubscriptionSync', () => ({
  useSubscriptionStatus: vi.fn(),
  useSyncConfigs: vi.fn(),
  useEnableSyncForJob: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', async () => {
  const React = await import('react');
  return { GlobalHeader: () => React.createElement('header') };
});

vi.mock('@/components/ux/TrackMappings', async () => {
  const React = await import('react');
  return {
    default: () =>
      React.createElement('div', { 'data-testid': 'track-mappings' }),
  };
});

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const me: User = {
  id: 1,
  supabaseId: 'sb-1',
  displayName: 'Sam',
  email: 'sam@example.com',
};

const baseJob: Job = {
  id: 7,
  provider: 'apple_music',
  targetProvider: 'apple_music',
  jobType: 'clean',
  swapExplicitForClean: true,
  sourcePlaylistId: 'p.roadtrip',
  sourcePlaylistName: 'Road Trip',
  // Library playlist id — the common Apple case, which has no public URL.
  targetPlaylistId: 'p.clean-roadtrip',
  targetPlaylistName: 'Clean - Road Trip',
  status: 'Completed',
  totalTracks: 42,
  processedTracks: 42,
  matchedTracks: 40,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:05:00Z',
};

const renderJob = (job: Job) => {
  vi.mocked(getJobDetails).mockResolvedValue(job);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <JobDetailsClient initialMe={me} initialJob={job} jobId={job.id} />
    </QueryClientProvider>
  );
};

const mockEnableSync = (overrides = {}) =>
  vi.mocked(useEnableSyncForJob).mockReturnValue({
    mutateAsync: vi.fn().mockResolvedValue({}),
    isPending: false,
    ...overrides,
  } as unknown as ReturnType<typeof useEnableSyncForJob>);

beforeEach(() => {
  vi.mocked(useSubscriptionStatus).mockReturnValue({
    data: { hasActiveSubscription: false },
    isLoading: false,
  } as ReturnType<typeof useSubscriptionStatus>);
  vi.mocked(useSyncConfigs).mockReturnValue({
    data: [],
  } as unknown as ReturnType<typeof useSyncConfigs>);
  mockEnableSync();
});

describe('JobDetailsClient', () => {
  it('shows a queued message and an indeterminate bar for a pending job', () => {
    renderJob({
      ...baseJob,
      status: 'Pending',
      totalTracks: 0,
      processedTracks: 0,
      matchedTracks: 0,
      targetPlaylistId: undefined,
    });

    expect(screen.getByText(/queued/i)).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });

  it('shows live counts and a progress bar while processing', () => {
    renderJob({ ...baseJob, status: 'Processing', processedTracks: 21 });

    expect(screen.getByText(/21 of 42 tracks processed/i)).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });

  it('surfaces the error message when the job failed', () => {
    renderJob({
      ...baseJob,
      status: 'Failed',
      errorMessage: 'Apple Music rejected the request (rate limited).',
    });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/this job failed/i);
    expect(alert).toHaveTextContent(/rate limited/i);
    expect(alert).toHaveTextContent(/original playlist is untouched/i);
  });

  it('falls back to honest generic copy when a failed job has no message', () => {
    renderJob({ ...baseJob, status: 'Failed', errorMessage: undefined });

    expect(screen.getByRole('alert')).toHaveTextContent(
      /something went wrong on our side/i
    );
  });

  it('points completed library playlists at the Apple Music app, not a dead link', () => {
    renderJob(baseJob);

    expect(
      screen.getByText(/ready in your apple music library/i)
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: /open in apple music/i })
    ).not.toBeInTheDocument();
  });

  it('links out when the completed playlist has a public URL', () => {
    renderJob({ ...baseJob, targetPlaylistId: 'pl.catalog123' });

    expect(
      screen.getByRole('link', { name: /open in apple music/i })
    ).toHaveAttribute(
      'href',
      'https://music.apple.com/library/playlist/pl.catalog123'
    );
  });

  it('offers the subscription page to free users after completion', () => {
    renderJob(baseJob);

    expect(
      screen.getByRole('link', { name: /see auto-sync/i })
    ).toBeInTheDocument();
  });

  it('lets a subscriber turn on Auto-Sync', async () => {
    const user = userEvent.setup();
    vi.mocked(useSubscriptionStatus).mockReturnValue({
      data: { hasActiveSubscription: true },
      isLoading: false,
    } as ReturnType<typeof useSubscriptionStatus>);
    const mutateAsync = vi.fn().mockResolvedValue({});
    mockEnableSync({ mutateAsync });
    renderJob(baseJob);

    await user.click(
      screen.getByRole('button', { name: /turn on auto-sync/i })
    );

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith(baseJob.id));
    expect(toast.success).toHaveBeenCalled();
  });

  it("surfaces the API's own explanation when enabling sync is refused", async () => {
    const user = userEvent.setup();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.mocked(useSubscriptionStatus).mockReturnValue({
      data: { hasActiveSubscription: true },
      isLoading: false,
    } as ReturnType<typeof useSubscriptionStatus>);
    mockEnableSync({
      mutateAsync: vi
        .fn()
        .mockRejectedValue(
          new ApiError(
            403,
            'Plan limit exceeded',
            'Your plan allows up to 10 synced playlists.'
          )
        ),
    });
    renderJob(baseJob);

    await user.click(
      screen.getByRole('button', { name: /turn on auto-sync/i })
    );

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Your plan allows up to 10 synced playlists.'
      )
    );
  });

  it('shows the sync status and a manage link once sync is enabled', () => {
    vi.mocked(useSyncConfigs).mockReturnValue({
      data: [
        {
          id: 3,
          originalJobId: baseJob.id,
          syncFrequency: 'Daily',
          lastSyncedAt: '2026-08-06T00:00:00Z',
          nextScheduledSync: '2026-08-08T00:00:00Z',
        },
      ],
    } as unknown as ReturnType<typeof useSyncConfigs>);
    renderJob(baseJob);

    expect(screen.getByText(/auto-sync is on/i)).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /manage auto-sync/i })
    ).toHaveAttribute('href', '/dashboard/sync');
    expect(
      screen.queryByRole('button', { name: /turn on auto-sync/i })
    ).not.toBeInTheDocument();
  });

  it('keeps the sync pitch off non-completed jobs', () => {
    renderJob({ ...baseJob, status: 'Processing', processedTracks: 10 });

    expect(screen.queryByText(/keep this copy current/i)).toBeNull();
    expect(screen.queryByRole('link', { name: /see auto-sync/i })).toBeNull();
  });
});
