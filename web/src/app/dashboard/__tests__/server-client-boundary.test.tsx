import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { DashboardClient } from '../dashboard-client';
import { QueryWrapper } from '@/test-utils/react-query-wrapper';
import { 
  testServerContext, 
  testClientContext, 
  testSerializability,
} from '@/test-utils/context-testing';
import { mockAuthenticatedSession } from '@/test-utils/supabase-test-client';

// Mock data that matches production structure
const mockServerUser = {
  id: 'test-user-id',
  email: 'test@example.com',
  created_at: '2024-01-01T00:00:00Z',
  updated_at: '2024-01-01T00:00:00Z',
  aud: 'authenticated',
  role: 'authenticated',
};

const mockMe = {
  id: 1,
  spotifyId: 'spotify123',
  displayName: 'Test User',
  email: 'test@example.com'
};

const mockPlaylists = [
  {
    id: 'playlist123',
    name: 'Test Playlist',
    trackCount: 10,
    ownerId: 'spotify123'
  }
];

const mockJobs: any[] = [];

describe('Server-Client Boundary Error - Supabase Integration', () => {
  
  beforeEach(() => {
    // Reset environment for each test
    vi.clearAllMocks();
    delete (globalThis as any).window;
  });

  it('should demonstrate server client creates non-serializable objects', async () => {
    // Test 1: Server client serialization
    const serverClient = await testServerContext();
    
    const serverSerializationTest = testSerializability(serverClient);
    expect(serverSerializationTest.success).toBe(false);
    expect(serverSerializationTest.error).toMatch(/circular structure|Converting circular structure/i);
  });

  it('should demonstrate client client creates serializable objects', () => {
    // Test 2: Client client serialization
    const clientClient = testClientContext();
    
    const clientSerializationTest = testSerializability(clientClient);
    expect(clientSerializationTest.success).toBe(true);
  });

  it('should reproduce the exact error when server client is used in client context', async () => {
    // Mock the problematic scenario - server client being used in client context
    vi.doMock('@/lib/supabase/server', () => ({
      createClient: vi.fn(() => {
        // Return a mock that mimics server client with non-serializable properties
        const mockServerClient = {
          auth: {
            getSession: vi.fn(() => Promise.resolve({
              data: { session: mockAuthenticatedSession }
            }))
          },
          // Add non-serializable properties that cause the error
          [Symbol.toStringTag]: 'SupabaseClient',
          _internal: function() {}, // Function reference causes serialization issues
          _listeners: new Map(), // Map object causes serialization issues
        };
        
        // Make it non-serializable by adding circular reference
        (mockServerClient as any).self = mockServerClient;
        
        return mockServerClient;
      })
    }));

    // Set up to catch console errors that occur during rendering
    const originalConsoleError = console.error;
    const errorMessages: string[] = [];
    console.error = (...args) => {
      errorMessages.push(args.join(' '));
    };

    // Mock fetch to ensure we don't actually call API
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(() => 
      Promise.resolve(new Response('{}', { status: 200 }))
    );

    try {
      render(
        <QueryWrapper>
          <DashboardClient
            serverUser={mockServerUser}
            initialMe={mockMe}
            initialPlaylists={mockPlaylists}
            initialJobs={mockJobs}
          />
        </QueryWrapper>
      );

      // Wait for component to render
      await waitFor(() => {
        expect(screen.getByText('Create a Clean Playlist')).toBeInTheDocument();
      });

      // Trigger the problematic mutation
      const playlistSelect = screen.getByRole('combobox');
      fireEvent.change(playlistSelect, { target: { value: 'playlist123' } });

      const createButton = screen.getByText('Create Clean Version');
      expect(createButton).toBeEnabled();

      // This should trigger the serialization error
      fireEvent.click(createButton);

      // Wait for potential error to occur
      await waitFor(() => {
        // Check if error occurred (may be in different forms)
        const hasSerializationError = errorMessages.some(msg => 
          msg.includes('Only plain objects, and a few built-ins, can be passed to Client Components') ||
          msg.includes('Converting circular structure to JSON') ||
          msg.includes('4187410481')
        );
        
        if (!hasSerializationError && errorMessages.length > 0) {
          console.log('Captured errors:', errorMessages);
        }
        
        // The key assertion - either we get the exact error or a related serialization error
        expect(hasSerializationError || errorMessages.some(msg => msg.includes('circular'))).toBe(true);
      }, { timeout: 3000 });

      // Verify fetch was not called (error happens before API)
      expect(fetchSpy).not.toHaveBeenCalled();

    } finally {
      console.error = originalConsoleError;
      fetchSpy.mockRestore();
    }
  });

  it('should verify error occurs before API call', async () => {
    // Mock fetch to track if API is called
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(() => 
      Promise.resolve(new Response('{}', { status: 200 }))
    );

    // Mock problematic server client
    vi.doMock('@/lib/supabase/server', () => ({
      createClient: () => {
        const badClient = { auth: { getSession: () => Promise.resolve({ data: { session: mockAuthenticatedSession } }) } };
        (badClient as any).circular = badClient; // Create circular reference
        return badClient;
      }
    }));

    const originalConsoleError = console.error;
    console.error = vi.fn();

    try {
      render(
        <QueryWrapper>
          <DashboardClient
            serverUser={mockServerUser}
            initialMe={mockMe}
            initialPlaylists={mockPlaylists}
            initialJobs={mockJobs}
          />
        </QueryWrapper>
      );

      const playlistSelect = screen.getByRole('combobox');
      fireEvent.change(playlistSelect, { target: { value: 'playlist123' } });

      const createButton = screen.getByText('Create Clean Version');
      fireEvent.click(createButton);

      await waitFor(() => {
        // Verify fetch was never called (error happens before API)
        expect(fetchSpy).not.toHaveBeenCalled();
      });

    } finally {
      console.error = originalConsoleError;
      fetchSpy.mockRestore();
    }
  });
});