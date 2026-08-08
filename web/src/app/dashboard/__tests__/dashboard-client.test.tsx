import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { User as SupabaseUser } from '@supabase/supabase-js';
import { describe, it, expect, vi, beforeEach } from 'vitest';

import { DashboardClient } from '../dashboard-client';
import {
  ApiError,
  createCleanPlaylistJob,
  getMe,
  getUserJobs,
  getUserPlaylists,
  type Job,
  type Playlist,
  type User,
} from '@/services/api';
import { useSubscriptionStatus } from '@/hooks/useSubscriptionSync';
import { toast } from 'sonner';

// Whether the stubbed connection card reports Apple Music as connected.
const connection = vi.hoisted(() => ({ connected: true }));

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
  getMe: vi.fn(),
  getUserPlaylists: vi.fn(),
  getUserJobs: vi.fn(),
  createCleanPlaylistJob: vi.fn(),
}));

vi.mock('@/hooks/useSubscriptionSync', () => ({
  useSubscriptionStatus: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', async () => {
  const React = await import('react');
  return { GlobalHeader: () => React.createElement('header') };
});

vi.mock('@/components/ProviderConnectionStatus', async () => {
  const React = await import('react');
  return {
    ProviderConnectionStatus: ({
      onConnectionChange,
    }: {
      onConnectionChange?: (connected: boolean) => void;
    }) => {
      React.useEffect(() => {
        onConnectionChange?.(connection.connected);
      }, [onConnectionChange]);
      return React.createElement('div', { 'data-testid': 'connection-card' });
    },
  };
});

vi.mock('@/components/ux/JobCard', async () => {
  const React = await import('react');
  return {
    JobCard: ({ job }: { job: Job }) =>
      React.createElement('div', null, job.targetPlaylistName),
  };
});

vi.mock('next/image', async () => {
  const React = await import('react');
  return {
    default: ({
      fill: _fill,
      priority: _priority,
      ...props
    }: Record<string, unknown>) => React.createElement('img', props),
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

const playlists: Playlist[] = [
  { id: 'p.roadtrip', name: 'Road Trip', trackCount: 42, ownerId: 'me' },
  { id: 'p.gym', name: 'Gym', trackCount: 0, ownerId: 'me' },
];

const completedJob: Job = {
  id: 7,
  provider: 'apple_music',
  targetProvider: 'apple_music',
  jobType: 'clean',
  swapExplicitForClean: true,
  sourcePlaylistId: 'p.roadtrip',
  sourcePlaylistName: 'Road Trip',
  targetPlaylistName: 'Clean - Road Trip',
  status: 'Completed',
  totalTracks: 42,
  processedTracks: 42,
  matchedTracks: 40,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T00:05:00Z',
};

const renderDashboard = ({
  initialJobs = [] as Job[],
  initialPlaylists = playlists as
    | Playlist[]
    | { error: string; message: string; playlists: Playlist[] },
} = {}) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <DashboardClient
        serverUser={{ id: 'sb-1' } as SupabaseUser}
        initialMe={me}
        initialPlaylists={initialPlaylists}
        initialJobs={initialJobs}
      />
    </QueryClientProvider>
  );
};

beforeEach(() => {
  connection.connected = true;
  vi.mocked(getMe).mockResolvedValue(me);
  vi.mocked(getUserPlaylists).mockResolvedValue(playlists);
  vi.mocked(getUserJobs).mockResolvedValue([]);
  vi.mocked(useSubscriptionStatus).mockReturnValue({
    data: { hasActiveSubscription: false },
  } as ReturnType<typeof useSubscriptionStatus>);
});

describe('DashboardClient', () => {
  it('renders each playlist exactly once', async () => {
    renderDashboard();

    // The old screen rendered every playlist twice — a desktop tree and a
    // mobile tree with only CSS hiding one.
    const cards = await screen.findAllByRole('button', { name: /road trip/i });
    expect(cards).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: /gym/i })).toHaveLength(1);
  });

  it('offers a single clean action — no copy destination, no explicit-swap choice', () => {
    renderDashboard();

    expect(screen.queryAllByRole('radio')).toHaveLength(0);
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    expect(
      screen.getByRole('button', { name: /make the clean copy/i })
    ).toBeInTheDocument();
  });

  it('selecting a playlist card fills the form selection', async () => {
    const user = userEvent.setup();
    renderDashboard();

    await user.click(await screen.findByRole('button', { name: /road trip/i }));

    expect(screen.getByRole('button', { name: /road trip/i })).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    expect(
      (screen.getByLabelText(/^playlist$/i) as HTMLSelectElement).value
    ).toBe('p.roadtrip');
  });

  it('creates an apple_music clean job and resets the form on success', async () => {
    const user = userEvent.setup();
    vi.mocked(createCleanPlaylistJob).mockResolvedValue(completedJob);
    renderDashboard();

    await user.click(await screen.findByRole('button', { name: /road trip/i }));
    await user.type(
      screen.getByLabelText(/name for the copy/i),
      '  Family Car Mix  '
    );
    await user.click(
      screen.getByRole('button', { name: /make the clean copy/i })
    );

    await waitFor(() =>
      expect(createCleanPlaylistJob).toHaveBeenCalledWith(me.id, {
        sourcePlaylistId: 'p.roadtrip',
        targetPlaylistName: 'Family Car Mix',
        provider: 'apple_music',
        swapExplicitForClean: true,
      })
    );
    expect(toast.success).toHaveBeenCalled();
    expect(
      (screen.getByLabelText(/^playlist$/i) as HTMLSelectElement).value
    ).toBe('');
  });

  it('omits the name so the server default applies when left blank', async () => {
    const user = userEvent.setup();
    vi.mocked(createCleanPlaylistJob).mockResolvedValue(completedJob);
    renderDashboard();

    await user.click(await screen.findByRole('button', { name: /road trip/i }));
    await user.click(
      screen.getByRole('button', { name: /make the clean copy/i })
    );

    await waitFor(() =>
      expect(createCleanPlaylistJob).toHaveBeenCalledWith(
        me.id,
        expect.objectContaining({ targetPlaylistName: undefined })
      )
    );
  });

  it('surfaces the API error message when job creation fails', async () => {
    const user = userEvent.setup();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.mocked(createCleanPlaylistJob).mockRejectedValue(
      new ApiError(429, 'You already have a job running for this playlist.')
    );
    renderDashboard();

    await user.click(await screen.findByRole('button', { name: /road trip/i }));
    await user.click(
      screen.getByRole('button', { name: /make the clean copy/i })
    );

    expect(
      await screen.findByText(/already have a job running/i)
    ).toBeInTheDocument();
  });

  it('shows connect guidance instead of playlists when Apple Music is not connected', () => {
    connection.connected = false;
    renderDashboard();

    expect(screen.getByText(/connect apple music above/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /road trip/i })
    ).not.toBeInTheDocument();
  });

  it('pitches Auto-Sync to a free user with a completed job', () => {
    renderDashboard({ initialJobs: [completedJob] });

    expect(
      screen.getByRole('link', { name: /see auto-sync/i })
    ).toBeInTheDocument();
  });

  it('never pitches Auto-Sync to subscribers or before the subscription is known', () => {
    vi.mocked(useSubscriptionStatus).mockReturnValue({
      data: { hasActiveSubscription: true },
    } as ReturnType<typeof useSubscriptionStatus>);
    const { unmount } = renderDashboard({ initialJobs: [completedJob] });
    expect(
      screen.queryByRole('link', { name: /see auto-sync/i })
    ).not.toBeInTheDocument();
    unmount();

    // Still loading — saying nothing beats flashing an upsell at a payer.
    vi.mocked(useSubscriptionStatus).mockReturnValue({
      data: undefined,
    } as ReturnType<typeof useSubscriptionStatus>);
    renderDashboard({ initialJobs: [completedJob] });
    expect(
      screen.queryByRole('link', { name: /see auto-sync/i })
    ).not.toBeInTheDocument();
  });
});
