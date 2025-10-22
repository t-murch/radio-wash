'use client';

import * as Sentry from '@sentry/nextjs';
import type { User as ApiUser } from '@/services/api';

export interface SentryUserContext {
  id: string;
  email?: string;
  username?: string;
}

export function setSentryUser(user: ApiUser | null): void {
  if (user) {
    const sentryUser: SentryUserContext = {
      id: String(user.id),
      email: user.email,
      username: user.displayName,
    };

    Sentry.setUser(sentryUser);
  } else {
    // Clear user context when user logs out
    Sentry.setUser(null);
  }
}

export function clearSentryUser(): void {
  Sentry.setUser(null);
}

