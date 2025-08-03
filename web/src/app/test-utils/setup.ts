import '@testing-library/jest-dom';
import { vi, beforeEach } from 'vitest';

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

// Mock environment detection
Object.defineProperty(globalThis, 'window', {
  value: {
    location: {
      href: 'http://localhost:3000',
    },
  },
  writable: true,
});

beforeEach(() => {
  // Reset all mocks before each test
  vi.clearAllMocks();
  
  // Reset fetch mock
  (global.fetch as any).mockClear();
});