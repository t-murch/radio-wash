'use client';

import { useEffect } from 'react';
import posthog from 'posthog-js';

import { createClient } from '@/lib/supabase/client';

const projectToken = process.env.NEXT_PUBLIC_POSTHOG_PROJECT_TOKEN;
const host = process.env.NEXT_PUBLIC_POSTHOG_HOST;

export function PostHogClient({ children }: { children: React.ReactNode }) {
  useEffect(() => {
    if (!projectToken || !host) {
      if (process.env.NODE_ENV === 'development') {
        console.warn(
          'PostHog is disabled: set NEXT_PUBLIC_POSTHOG_PROJECT_TOKEN and NEXT_PUBLIC_POSTHOG_HOST to capture analytics events. Until then, all capture calls are silently dropped.'
        );
      }
      return;
    }

    if (!posthog.__loaded) {
      posthog.init(projectToken, {
        api_host: host,
        defaults: '2026-01-30',
        capture_exceptions: {
          capture_unhandled_errors: true,
          capture_unhandled_rejections: true,
          capture_console_errors: false,
        },
      });
    }

    const supabase = createClient();

    const identifyUser = (user: {
      id: string;
      email?: string;
      user_metadata: Record<string, unknown>;
    }) => {
      // Identify-over-identify is a merge PostHog's server refuses; reset first
      // when a different user was identified on this browser. An anonymous
      // distinct_id must NOT be reset — identify() links it to the person.
      if (posthog._isIdentified() && posthog.get_distinct_id() !== user.id) {
        posthog.reset();
      }

      const properties: Record<string, string> = {};
      if (user.email) properties.email = user.email;

      const name = user.user_metadata.full_name ?? user.user_metadata.name;
      if (typeof name === 'string' && name) properties.name = name;

      posthog.identify(user.id, properties);
    };

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((event, session) => {
      if (event === 'SIGNED_OUT') {
        posthog.reset();
        return;
      }

      // INITIAL_SESSION fires on subscribe with any restored session, so no
      // separate getSession() call (which rejects when storage is blocked).
      if (
        (event === 'INITIAL_SESSION' || event === 'SIGNED_IN') &&
        session?.user
      ) {
        identifyUser(session.user);
      }
    });

    return () => subscription.unsubscribe();
  }, []);

  return children;
}
