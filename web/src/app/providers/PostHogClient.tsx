'use client';

import { useEffect, useRef } from 'react';
import posthog from 'posthog-js';

import { createClient } from '@/lib/supabase/client';

let isInitialized = false;

export function PostHogClient({ children }: { children: React.ReactNode }) {
  const projectToken = process.env.NEXT_PUBLIC_POSTHOG_PROJECT_TOKEN;
  const host = process.env.NEXT_PUBLIC_POSTHOG_HOST;
  const identifiedUserId = useRef<string | null>(null);

  const isConfigured = Boolean(projectToken && host);

  if (projectToken && host && !isInitialized) {
    posthog.init(projectToken, {
      api_host: host,
      defaults: '2026-01-30',
      capture_exceptions: {
        capture_unhandled_errors: true,
        capture_unhandled_rejections: true,
        capture_console_errors: false,
      },
    });
    isInitialized = true;
  }

  useEffect(() => {
    if (!isConfigured) return;

    const supabase = createClient();

    const identifyUser = (user: {
      id: string;
      email?: string;
      user_metadata: Record<string, unknown>;
    }) => {
      if (identifiedUserId.current && identifiedUserId.current !== user.id) {
        posthog.reset();
      }

      const properties: Record<string, string> = {};
      if (user.email) properties.email = user.email;

      const name = user.user_metadata.full_name ?? user.user_metadata.name;
      if (typeof name === 'string' && name) properties.name = name;

      posthog.identify(user.id, properties);
      identifiedUserId.current = user.id;
    };

    void supabase.auth.getSession().then(({ data: { session } }) => {
      if (session?.user) identifyUser(session.user);
    });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((event, session) => {
      if (event === 'SIGNED_OUT') {
        posthog.reset();
        identifiedUserId.current = null;
        return;
      }

      if (event === 'SIGNED_IN' && session?.user) identifyUser(session.user);
    });

    return () => subscription.unsubscribe();
  }, [isConfigured]);

  if (!projectToken && process.env.NODE_ENV === 'development') {
    throw new Error(
      'NEXT_PUBLIC_POSTHOG_PROJECT_TOKEN variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once NEXT_PUBLIC_POSTHOG_PROJECT_TOKEN is configured'
    );
  }

  if (!host && process.env.NODE_ENV === 'development') {
    throw new Error(
      'NEXT_PUBLIC_POSTHOG_HOST variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once NEXT_PUBLIC_POSTHOG_HOST is configured'
    );
  }

  return children;
}
