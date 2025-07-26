import { fetchWithSupabaseAuth, createCleanPlaylistJob, getMe, getUserPlaylists } from '../api';
import { mockAuthenticatedSession } from '@/test-utils/supabase-test-client';

// Mock the Supabase clients
vi.mock('@/lib/supabase/server', () => ({
  createClient: vi.fn(),
}));

vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(),
}));

describe('API Functions', () => {
  
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset fetch mock
    global.fetch = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('fetchWithSupabaseAuth', () => {
    it('should handle valid Supabase session', async () => {
      // Mock client with valid session
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify({ success: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      );

      const result = await fetchWithSupabaseAuth('http://test.com/api/test');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://test.com/api/test',
        expect.objectContaining({
          headers: expect.objectContaining({
            'Authorization': 'Bearer mock-access-token'
          })
        })
      );

      expect(result).toEqual({ success: true });
    });

    it('should handle missing Supabase session', async () => {
      // Mock client with no session
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: null }
          }))
        }
      });

      await expect(
        fetchWithSupabaseAuth('http://test.com/api/test')
      ).rejects.toThrow('User not authenticated');
    });

    it('should handle API error responses', async () => {
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response('Not Found', {
          status: 404,
          statusText: 'Not Found'
        })
      );

      await expect(
        fetchWithSupabaseAuth('http://test.com/api/test')
      ).rejects.toThrow('Request failed: Not Found');
    });
  });

  describe('createCleanPlaylistJob', () => {
    it('should make correct API call', async () => {
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockJob = {
        id: 1,
        sourcePlaylistId: 'playlist123',
        sourcePlaylistName: 'Test Playlist',
        status: 'pending',
        totalTracks: 10,
        processedTracks: 0,
        matchedTracks: 0,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z'
      };

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockJob), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      );

      const result = await createCleanPlaylistJob(1, 'playlist123', 'Clean Playlist');

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/cleanplaylist/user/1/job'),
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            'Authorization': 'Bearer mock-access-token'
          }),
          body: JSON.stringify({
            sourcePlaylistId: 'playlist123',
            targetPlaylistName: 'Clean Playlist'
          })
        })
      );

      expect(result).toEqual(mockJob);
    });
  });

  describe('getMe', () => {
    it('should fetch user data correctly', async () => {
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockUser = {
        id: 1,
        spotifyId: 'spotify123',
        displayName: 'Test User',
        email: 'test@example.com'
      };

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockUser), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      );

      const result = await getMe();

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/auth/me'),
        expect.objectContaining({
          headers: expect.objectContaining({
            'Authorization': 'Bearer mock-access-token'
          })
        })
      );

      expect(result).toEqual(mockUser);
    });
  });

  describe('getUserPlaylists', () => {
    it('should fetch playlists correctly', async () => {
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockPlaylists = [
        {
          id: 'playlist1',
          name: 'Test Playlist 1',
          trackCount: 10,
          ownerId: 'spotify123'
        },
        {
          id: 'playlist2',
          name: 'Test Playlist 2',
          trackCount: 5,
          ownerId: 'spotify123'
        }
      ];

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockPlaylists), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      );

      const result = await getUserPlaylists();

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/playlist/user/me'),
        expect.objectContaining({
          headers: expect.objectContaining({
            'Authorization': 'Bearer mock-access-token'
          })
        })
      );

      expect(result).toEqual(mockPlaylists);
    });

    it('should handle error response format', async () => {
      const mockCreateClient = await import('@/lib/supabase/server');
      (mockCreateClient.createClient as any).mockResolvedValue({
        auth: {
          getSession: vi.fn(() => Promise.resolve({
            data: { session: mockAuthenticatedSession }
          }))
        }
      });

      const mockErrorResponse = {
        error: 'Spotify not connected',
        message: 'Please connect your Spotify account',
        playlists: []
      };

      const mockFetch = global.fetch as any;
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockErrorResponse), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      );

      const result = await getUserPlaylists();
      expect(result).toEqual(mockErrorResponse);
    });
  });
});