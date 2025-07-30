import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { useMutation } from '@tanstack/react-query';
import { DashboardClient } from '../dashboard-client';
import { QueryWrapper } from '@/test-utils/react-query-wrapper';
import { mockAuthenticatedSession } from '@/test-utils/supabase-test-client';

// Mock the API functions
vi.mock('@/services/api', () => ({
  createCleanPlaylistJob: vi.fn(),
  getMe: vi.fn(),
  getUserPlaylists: vi.fn(),
  getUserJobs: vi.fn(),
}));

// Mock the Supabase client
vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(() => ({
    auth: {
      signOut: vi.fn(() => Promise.resolve())
    }
  }))
}));

const mockProps = {
  serverUser: {
    id: 'test-user-id',
    email: 'test@example.com',
    created_at: '2024-01-01T00:00:00Z'
  },
  initialMe: {
    id: 1,
    spotifyId: 'spotify123',
    displayName: 'Test User',
    email: 'test@example.com',
    profileImageUrl: 'https://example.com/avatar.jpg'
  },
  initialPlaylists: [
    {
      id: 'playlist1',
      name: 'Test Playlist 1',
      description: 'A test playlist',
      trackCount: 15,
      ownerId: 'spotify123',
      imageUrl: 'https://example.com/playlist1.jpg'
    },
    {
      id: 'playlist2',
      name: 'Test Playlist 2',
      description: 'Another test playlist',
      trackCount: 8,
      ownerId: 'spotify123',
      imageUrl: null
    }
  ],
  initialJobs: []
};

describe('DashboardClient Component Integration', () => {
  
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset console.error mock
    console.error = vi.fn();
  });

  it('should render dashboard correctly with mock data', async () => {
    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    // Check main elements are rendered
    expect(screen.getByText('RadioWash')).toBeInTheDocument();
    expect(screen.getByText('Create a Clean Playlist')).toBeInTheDocument();
    expect(screen.getByText('Welcome, Test User')).toBeInTheDocument();
    
    // Check playlists are rendered
    expect(screen.getByText('Test Playlist 1')).toBeInTheDocument();
    expect(screen.getByText('Test Playlist 2')).toBeInTheDocument();
    expect(screen.getByText('15 tracks')).toBeInTheDocument();
    expect(screen.getByText('8 tracks')).toBeInTheDocument();
  });

  it('should handle playlist selection and form interaction', async () => {
    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    // Initially button should be disabled
    const createButton = screen.getByText('Create Clean Version');
    expect(createButton).toBeDisabled();

    // Select a playlist
    const playlistSelect = screen.getByRole('combobox');
    fireEvent.change(playlistSelect, { target: { value: 'playlist1' } });

    // Button should now be enabled
    expect(createButton).toBeEnabled();

    // Enter custom name
    const nameInput = screen.getByPlaceholderText('New Playlist Name (Optional)');
    fireEvent.change(nameInput, { target: { value: 'My Clean Playlist' } });
    
    expect(nameInput).toHaveValue('My Clean Playlist');
  });

  it('should trigger mutation when create button is clicked', async () => {
    const { createCleanPlaylistJob } = await import('@/services/api');
    const mockMutation = vi.mocked(createCleanPlaylistJob);
    
    // Mock successful job creation
    mockMutation.mockResolvedValueOnce({
      id: 1,
      sourcePlaylistId: 'playlist1',
      sourcePlaylistName: 'Test Playlist 1',
      status: 'pending',
      totalTracks: 15,
      processedTracks: 0,
      matchedTracks: 0,
      createdAt: '2024-01-01T10:00:00Z',
      updatedAt: '2024-01-01T10:00:00Z'
    });

    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    // Select playlist and click create
    const playlistSelect = screen.getByRole('combobox');
    fireEvent.change(playlistSelect, { target: { value: 'playlist1' } });

    const createButton = screen.getByText('Create Clean Version');
    fireEvent.click(createButton);

    // Should show loading state
    await waitFor(() => {
      expect(screen.getByText('Working on it...')).toBeInTheDocument();
    });

    // Verify mutation was called with correct parameters
    await waitFor(() => {
      expect(mockMutation).toHaveBeenCalledWith(
        1, // user ID
        'playlist1', // source playlist ID
        'Clean - Test Playlist 1' // target name (default)
      );
    });
  });

  it('should use custom name when provided', async () => {
    const { createCleanPlaylistJob } = await import('@/services/api');
    const mockMutation = vi.mocked(createCleanPlaylistJob);
    
    mockMutation.mockResolvedValueOnce({
      id: 1,
      sourcePlaylistId: 'playlist1',
      sourcePlaylistName: 'Test Playlist 1',
      status: 'pending',
      totalTracks: 15,
      processedTracks: 0,
      matchedTracks: 0,
      createdAt: '2024-01-01T10:00:00Z',
      updatedAt: '2024-01-01T10:00:00Z'
    });

    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    // Select playlist and enter custom name
    const playlistSelect = screen.getByRole('combobox');
    fireEvent.change(playlistSelect, { target: { value: 'playlist1' } });

    const nameInput = screen.getByPlaceholderText('New Playlist Name (Optional)');
    fireEvent.change(nameInput, { target: { value: 'My Custom Clean Playlist' } });

    const createButton = screen.getByText('Create Clean Version');
    fireEvent.click(createButton);

    // Verify mutation was called with custom name
    await waitFor(() => {
      expect(mockMutation).toHaveBeenCalledWith(
        1,
        'playlist1',
        'My Custom Clean Playlist'
      );
    });
  });

  it('should handle mutation errors gracefully', async () => {
    const { createCleanPlaylistJob } = await import('@/services/api');
    const mockMutation = vi.mocked(createCleanPlaylistJob);
    
    // Mock mutation failure
    mockMutation.mockRejectedValueOnce(new Error('API Error'));

    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    const playlistSelect = screen.getByRole('combobox');
    fireEvent.change(playlistSelect, { target: { value: 'playlist1' } });

    const createButton = screen.getByText('Create Clean Version');
    fireEvent.click(createButton);

    // Should show loading state initially
    await waitFor(() => {
      expect(screen.getByText('Working on it...')).toBeInTheDocument();
    });

    // After error, should return to normal state
    await waitFor(() => {
      expect(screen.getByText('Create Clean Version')).toBeInTheDocument();
    }, { timeout: 3000 });
  });

  it('should handle empty playlists state', () => {
    const propsWithoutPlaylists = {
      ...mockProps,
      initialPlaylists: []
    };

    render(
      <QueryWrapper>
        <DashboardClient {...propsWithoutPlaylists} />
      </QueryWrapper>
    );

    expect(screen.getByText('No playlists found. Make sure you have playlists on Spotify.')).toBeInTheDocument();
  });

  it('should handle playlist error response format', () => {
    const propsWithPlaylistError = {
      ...mockProps,
      initialPlaylists: {
        error: 'Spotify not connected',
        message: 'Please connect your Spotify account',
        playlists: []
      }
    };

    render(
      <QueryWrapper>
        <DashboardClient {...propsWithPlaylistError} />
      </QueryWrapper>
    );

    // Should still render but with empty playlists
    expect(screen.getByText('Create a Clean Playlist')).toBeInTheDocument();
    expect(screen.getByText('No playlists found. Make sure you have playlists on Spotify.')).toBeInTheDocument();
  });

  it('should reset form after successful mutation', async () => {
    const { createCleanPlaylistJob } = await import('@/services/api');
    const mockMutation = vi.mocked(createCleanPlaylistJob);
    
    mockMutation.mockResolvedValueOnce({
      id: 1,
      sourcePlaylistId: 'playlist1',
      sourcePlaylistName: 'Test Playlist 1',
      status: 'pending',
      totalTracks: 15,
      processedTracks: 0,
      matchedTracks: 0,
      createdAt: '2024-01-01T10:00:00Z',
      updatedAt: '2024-01-01T10:00:00Z'
    });

    render(
      <QueryWrapper>
        <DashboardClient {...mockProps} />
      </QueryWrapper>
    );

    // Fill form
    const playlistSelect = screen.getByRole('combobox');
    fireEvent.change(playlistSelect, { target: { value: 'playlist1' } });

    const nameInput = screen.getByPlaceholderText('New Playlist Name (Optional)');
    fireEvent.change(nameInput, { target: { value: 'Test Name' } });

    const createButton = screen.getByText('Create Clean Version');
    fireEvent.click(createButton);

    // Wait for success and form reset
    await waitFor(() => {
      expect(playlistSelect).toHaveValue('');
      expect(nameInput).toHaveValue('');
      expect(createButton).toBeDisabled();
    }, { timeout: 3000 });
  });
});