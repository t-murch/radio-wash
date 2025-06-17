import { authManager } from '@/services/auth';
import { useQuery } from '@tanstack/react-query';

export function useAuthenticatedQuery<T>(
  key: string[],
  endpoint: string,
  options?: any
) {
  return useQuery({
    queryKey: key,
    queryFn: async (): Promise<T> => {
      const token = await authManager.getToken();
      if (!token) throw new Error('No authentication token');

      const response = await fetch(endpoint, {
        headers: { Authorization: `Bearer ${token}` },
        ...options,
      });

      if (!response.ok) {
        throw new Error(`API call failed: ${response.statusText}`);
      }

      return response.json();
    },
  });
}
