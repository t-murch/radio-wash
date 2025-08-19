import { renderHook, waitFor, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { useAuthToken } from '../useAuthToken';
import { createClient } from '@/lib/supabase/client';

// Mock Supabase client
vi.mock('@/lib/supabase/client', () => ({
  createClient: vi.fn(),
}));

describe('useAuthToken', () => {
  let mockSupabase: {
    auth: {
      getSession: Mock;
      onAuthStateChange: Mock;
    };
  };
  let mockSubscription: {
    unsubscribe: Mock;
  };

  beforeEach(() => {
    mockSubscription = {
      unsubscribe: vi.fn(),
    };

    mockSupabase = {
      auth: {
        getSession: vi.fn(),
        onAuthStateChange: vi.fn().mockReturnValue({
          data: { subscription: mockSubscription },
        }),
      },
    };

    (createClient as Mock).mockReturnValue(mockSupabase);
  });

  it('should return null token and loading state initially', async () => {
    mockSupabase.auth.getSession.mockResolvedValue({
      data: { session: null },
    });

    const { result } = renderHook(() => useAuthToken());

    expect(result.current.authToken).toBeNull();
    expect(result.current.isLoading).toBe(true);

    // Wait for the async getSession to complete
    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should return auth token when user is authenticated', async () => {
    const mockToken = 'mock-jwt-token';
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: mockToken,
        },
      },
    });

    const { result } = renderHook(() => useAuthToken());

    await waitFor(() => {
      expect(result.current.authToken).toBe(mockToken);
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should return null when session is null', async () => {
    mockSupabase.auth.getSession.mockResolvedValue({
      data: { session: null },
    });

    const { result } = renderHook(() => useAuthToken());

    await waitFor(() => {
      expect(result.current.authToken).toBeNull();
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should handle auth state changes correctly', async () => {
    const initialToken = 'initial-token';
    const newToken = 'new-token';

    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: initialToken,
        },
      },
    });

    const { result } = renderHook(() => useAuthToken());

    // Wait for initial session to load
    await waitFor(() => {
      expect(result.current.authToken).toBe(initialToken);
    });

    // Simulate auth state change wrapped in act
    const authStateChangeCallback = mockSupabase.auth.onAuthStateChange.mock.calls[0][0];
    
    await act(async () => {
      authStateChangeCallback('SIGNED_IN', {
        access_token: newToken,
      });
    });

    await waitFor(() => {
      expect(result.current.authToken).toBe(newToken);
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should handle sign out correctly', async () => {
    const initialToken = 'initial-token';

    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: initialToken,
        },
      },
    });

    const { result } = renderHook(() => useAuthToken());

    // Wait for initial session to load
    await waitFor(() => {
      expect(result.current.authToken).toBe(initialToken);
    });

    // Simulate sign out wrapped in act
    const authStateChangeCallback = mockSupabase.auth.onAuthStateChange.mock.calls[0][0];
    
    await act(async () => {
      authStateChangeCallback('SIGNED_OUT', null);
    });

    await waitFor(() => {
      expect(result.current.authToken).toBeNull();
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should handle getSession errors gracefully', async () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    
    mockSupabase.auth.getSession.mockRejectedValue(new Error('Network error'));

    const { result } = renderHook(() => useAuthToken());

    await waitFor(() => {
      expect(result.current.authToken).toBeNull();
      expect(result.current.isLoading).toBe(false);
      expect(consoleErrorSpy).toHaveBeenCalledWith(
        'Error fetching auth token:',
        expect.any(Error)
      );
    });

    consoleErrorSpy.mockRestore();
  });

  it('should unsubscribe from auth state changes on unmount', () => {
    mockSupabase.auth.getSession.mockResolvedValue({
      data: { session: null },
    });

    const { unmount } = renderHook(() => useAuthToken());

    unmount();

    expect(mockSubscription.unsubscribe).toHaveBeenCalled();
  });

  it('should handle multiple rapid auth state changes', async () => {
    mockSupabase.auth.getSession.mockResolvedValue({
      data: { session: null },
    });

    const { result } = renderHook(() => useAuthToken());

    // Wait for initial load
    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    const authStateChangeCallback = mockSupabase.auth.onAuthStateChange.mock.calls[0][0];

    // Simulate rapid auth state changes wrapped in act
    await act(async () => {
      authStateChangeCallback('SIGNED_IN', { access_token: 'token1' });
      authStateChangeCallback('TOKEN_REFRESHED', { access_token: 'token2' });
      authStateChangeCallback('SIGNED_OUT', null);
      authStateChangeCallback('SIGNED_IN', { access_token: 'token3' });
    });

    await waitFor(() => {
      expect(result.current.authToken).toBe('token3');
      expect(result.current.isLoading).toBe(false);
    });
  });

  it('should handle token refresh events', async () => {
    const initialToken = 'initial-token';
    const refreshedToken = 'refreshed-token';

    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          access_token: initialToken,
        },
      },
    });

    const { result } = renderHook(() => useAuthToken());

    await waitFor(() => {
      expect(result.current.authToken).toBe(initialToken);
    });

    // Simulate token refresh wrapped in act
    const authStateChangeCallback = mockSupabase.auth.onAuthStateChange.mock.calls[0][0];
    
    await act(async () => {
      authStateChangeCallback('TOKEN_REFRESHED', {
        access_token: refreshedToken,
      });
    });

    await waitFor(() => {
      expect(result.current.authToken).toBe(refreshedToken);
    });
  });

  it('should handle session without access_token gracefully', async () => {
    mockSupabase.auth.getSession.mockResolvedValue({
      data: {
        session: {
          // No access_token property
          user: { id: 'user-id' },
        },
      },
    });

    const { result } = renderHook(() => useAuthToken());

    await waitFor(() => {
      expect(result.current.authToken).toBeNull();
      expect(result.current.isLoading).toBe(false);
    });
  });
});