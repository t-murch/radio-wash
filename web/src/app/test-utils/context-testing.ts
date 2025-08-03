import { createClient as createServerClient } from '@/lib/supabase/server';
import { createClient as createClientClient } from '@/lib/supabase/client';

export const testServerContext = async () => {
  // Simulate server environment
  Object.defineProperty(globalThis, 'window', {
    value: undefined,
    writable: true,
  });
  
  const serverClient = await createServerClient();
  return serverClient;
};

export const testClientContext = () => {
  // Simulate browser environment
  Object.defineProperty(globalThis, 'window', {
    value: { location: { href: 'http://localhost:3000' } },
    writable: true,
  });
  
  const clientClient = createClientClient();
  return clientClient;
};

export const testSerializability = (obj: any) => {
  try {
    // Attempt JSON serialization (what Next.js does)
    const serialized = JSON.stringify(obj);
    const deserialized = JSON.parse(serialized);
    return { success: true, serialized: deserialized };
  } catch (error) {
    return { success: false, error: (error as Error).message };
  }
};