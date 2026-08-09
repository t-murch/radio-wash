import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import { SyncDashboardClient } from '../sync-dashboard-client';
import {
  ApiError,
  triggerManualSync,
  disableSync,
  type PlaylistSyncConfig,
  type User,
} from '@/services/api';
import {
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
  triggerManualSync: vi.fn(),
  disableSync: vi.fn(),
}));

vi.mock('@/hooks/useSubscriptionSync', () => ({
  useSubscriptionStatus: vi.fn(),
  useSyncConfigs: vi.fn(),
}));

vi.mock('@/components/GlobalHeader', async () => {
  const React = await import('react');
  return { GlobalHeader: () => React.createElement('header') };
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

const syncConfig: PlaylistSyncConfig = {
  id: 11,
  originalJobId: 7,
  sourcePlaylistId: 'p.roadtrip',
  sourcePlaylistName: 'Road Trip',
  targetPlaylistId: 'p.clean',
  targetPlaylistName: 'Clean - Road Trip',
  isActive: true,
  syncFrequency: 'daily',
  lastSyncedAt: '2026-08-08T06:00:00Z',
  lastSyncStatus: 'completed',
  nextScheduledSync: '2026-08-09T06:00:00Z',
  createdAt: '2026-08-01T00:00:00Z',
};

const renderSyncDashboard = ({
  hasActiveSubscription = true,
  configs = [syncConfig] as PlaylistSyncConfig[],
} = {}) => {
  vi.mocked(useSubscriptionStatus).mockReturnValue({
    data: { hasActiveSubscription },
    isLoading: false,
  } as ReturnType<typeof useSubscriptionStatus>);
  vi.mocked(useSyncConfigs).mockReturnValue({
    data: configs,
    isLoading: false,
  } as ReturnType<typeof useSyncConfigs>);

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <SyncDashboardClient initialUser={me} />
    </QueryClientProvider>
  );
};

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('SyncDashboardClient', () => {
  it('states the additive-only contract as first-class copy', () => {
    renderSyncDashboard();

    expect(screen.getByText('Auto-Sync only ever adds.')).toBeInTheDocument();
    expect(
      screen.getByText(/your copy keeps its clean version/i)
    ).toBeInTheDocument();
  });

  it('shows the locked pitch with a link to the subscription page for free users', () => {
    renderSyncDashboard({ hasActiveSubscription: false, configs: [] });

    expect(
      screen.getByText('Auto-Sync is part of the Sync Plan')
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'See Auto-Sync' })).toHaveAttribute(
      'href',
      '/subscription'
    );
    // The additive-only contract is stated even before subscribing.
    expect(screen.getByText('Auto-Sync only ever adds.')).toBeInTheDocument();
  });

  it('points subscribers with no configs at their playlists', () => {
    renderSyncDashboard({ configs: [] });

    expect(screen.getByText('Nothing is syncing yet')).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Go to your playlists' })
    ).toHaveAttribute('href', '/dashboard');
  });

  it('renders a card per syncing playlist with its status', () => {
    renderSyncDashboard();

    expect(screen.getByText('Clean - Road Trip')).toBeInTheDocument();
    expect(screen.getByText('From Road Trip')).toBeInTheDocument();
    expect(screen.getByText('Up to date')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View playlist' })).toHaveAttribute(
      'href',
      '/jobs/7'
    );
  });

  it('reports added tracks without ever claiming removals', async () => {
    const user = userEvent.setup();
    vi.mocked(triggerManualSync).mockResolvedValue({
      success: true,
      tracksAdded: 3,
      tracksUnchanged: 42,
      executionTimeMs: 1200,
    });
    renderSyncDashboard();

    await user.click(screen.getByRole('button', { name: 'Check now' }));

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith(
        'Added 3 clean versions to your copy.'
      );
    });
    expect(vi.mocked(toast.success).mock.calls[0][0]).not.toMatch(/remov/i);
  });

  it('says the copy is already up to date when a check adds nothing', async () => {
    const user = userEvent.setup();
    vi.mocked(triggerManualSync).mockResolvedValue({
      success: true,
      tracksAdded: 0,
      tracksUnchanged: 42,
      executionTimeMs: 800,
    });
    renderSyncDashboard();

    await user.click(screen.getByRole('button', { name: 'Check now' }));

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith(
        'Already up to date — nothing new to add.'
      );
    });
  });

  it('surfaces the API detail when a manual check hits a plan limit', async () => {
    const user = userEvent.setup();
    vi.mocked(triggerManualSync).mockRejectedValue(
      new ApiError(403, 'Forbidden', 'Your plan allows 10 synced playlists.')
    );
    renderSyncDashboard();

    await user.click(screen.getByRole('button', { name: 'Check now' }));

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Your plan allows 10 synced playlists.'
      );
    });
  });

  it('turns off sync only after the user confirms, and promises the copy is untouched', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.fn(() => true);
    vi.stubGlobal('confirm', confirmSpy);
    vi.mocked(disableSync).mockResolvedValue({ success: true });
    renderSyncDashboard();

    await user.click(screen.getByRole('button', { name: 'Turn off' }));

    expect(confirmSpy).toHaveBeenCalledWith(
      expect.stringContaining('The copy keeps everything it has')
    );
    await waitFor(() => {
      expect(disableSync).toHaveBeenCalledWith(11);
      expect(toast.success).toHaveBeenCalledWith(
        'Auto-Sync is off for that playlist. Your copy stays as it is.'
      );
    });
  });

  it('does not turn off sync when the user cancels the confirmation', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'confirm',
      vi.fn(() => false)
    );
    renderSyncDashboard();

    await user.click(screen.getByRole('button', { name: 'Turn off' }));

    expect(disableSync).not.toHaveBeenCalled();
  });
});
