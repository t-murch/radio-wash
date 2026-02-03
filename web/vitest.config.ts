import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/app/test-utils/setup.ts'],
    globals: true,
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src/app'),
    },
  },
  define: {
    // Mock environment variables for tests
    'process.env.NEXT_PUBLIC_SUPABASE_URL': '"http://localhost:54321"',
    'process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY': '"test-key"',
    'process.env.API_BASE_URL': '"http://localhost:5159"',
    'process.env.NEXT_PUBLIC_API_URL': '"http://localhost:5159"',
  },
});