import { API_BASE_URL } from '@/services/api';
import { useQuery } from '@tanstack/react-query';

export function useUser() {
  return useQuery({
    queryKey: ['user'],
    queryFn: async () => {
      const response = await fetch(`${API_BASE_URL}/api/user`, {
        credentials: 'include',
      });
      if (!response.ok) {
        throw new Error('Failed to fetch user');
      }
      return response.json();
    },
  });
}
