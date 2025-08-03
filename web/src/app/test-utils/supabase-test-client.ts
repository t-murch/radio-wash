import { createClient } from '@supabase/supabase-js';

export const createTestClient = () => {
  return createClient(
    'http://localhost:54321',
    'test-key',
    {
      auth: {
        // Disable auto refresh for tests
        autoRefreshToken: false,
        persistSession: false,
      },
    }
  );
};

// Mock authenticated session
export const mockAuthenticatedSession = {
  access_token: 'mock-access-token',
  refresh_token: 'mock-refresh-token',
  expires_in: 3600,
  token_type: 'bearer' as const,
  user: {
    id: 'mock-user-id',
    email: 'test@example.com',
    created_at: '2024-01-01T00:00:00Z',
    updated_at: '2024-01-01T00:00:00Z',
    aud: 'authenticated',
    role: 'authenticated',
    app_metadata: {},
    user_metadata: {},
  },
};