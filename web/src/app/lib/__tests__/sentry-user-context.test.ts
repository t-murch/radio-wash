import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { User as ApiUser } from '@/services/api';

// Mock Sentry
vi.mock('@sentry/nextjs', () => ({
  setUser: vi.fn(),
}));

import { setSentryUser, clearSentryUser } from '../sentry-user-context';
import * as Sentry from '@sentry/nextjs';

describe('sentry-user-context', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('setSentryUser', () => {
    it('should set user context when user is provided', () => {
      const mockUser: ApiUser = {
        id: 'user-123',
        email: 'test@example.com',
        displayName: 'Test User',
        profileImageUrl: 'https://example.com/avatar.jpg',
      };

      setSentryUser(mockUser);

      expect(Sentry.setUser).toHaveBeenCalledWith({
        id: 'user-123',
        email: 'test@example.com',
        username: 'Test User',
      });
    });

    it('should clear user context when user is null', () => {
      setSentryUser(null);

      expect(Sentry.setUser).toHaveBeenCalledWith(null);
    });

    it('should handle user with missing optional fields', () => {
      const mockUser: ApiUser = {
        id: 'user-456',
        email: 'minimal@example.com',
        displayName: 'Minimal User',
        profileImageUrl: null,
      };

      setSentryUser(mockUser);

      expect(Sentry.setUser).toHaveBeenCalledWith({
        id: 'user-456',
        email: 'minimal@example.com',
        username: 'Minimal User',
      });
    });
  });

  describe('clearSentryUser', () => {
    it('should clear user context', () => {
      clearSentryUser();

      expect(Sentry.setUser).toHaveBeenCalledWith(null);
    });
  });
});