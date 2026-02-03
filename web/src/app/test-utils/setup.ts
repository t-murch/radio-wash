import '@testing-library/jest-dom';
import { cleanup } from '@testing-library/react';
import { vi, beforeEach, afterEach } from 'vitest';

// Global mocks
global.fetch = vi.fn();

// Mock Next.js router
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    refresh: vi.fn(),
    push: vi.fn(),
    replace: vi.fn(),
  }),
  redirect: vi.fn(),
}));

beforeEach(() => {
  // Reset all mocks before each test
  vi.clearAllMocks();

  // Reset fetch mock
  (global.fetch as any).mockClear();
});

afterEach(() => {
  // Clean up React Testing Library renders to prevent state leaks between tests
  cleanup();
});
