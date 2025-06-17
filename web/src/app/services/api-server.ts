'use server';

import { cookies } from 'next/headers';
import { API_BASE_URL, User } from './api';

export const handleCallbackServer = async (
  code: string,
  state: string
): Promise<{ token: string; user: User }> => {
  const response = await fetch(
    `${API_BASE_URL}/auth/callback?code=${code}&state=${state}`,
    { credentials: 'include' }
  );
  if (!response.ok) {
    throw new Error('Authentication failed');
  }
  const cookieStore = (await cookies()).getAll();

  console.log(`all cookies: ${JSON.stringify(cookieStore)}`);

  return await response.json();
};
