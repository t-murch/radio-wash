'use client';

import { useEffect, useState } from 'react';

import { logger } from '@/lib/logger';
import { createClient } from '@/lib/supabase/client';

/**
 * Whether a session exists, read in the browser.
 *
 * This trusts the local session instead of validating it against Supabase the
 * way the server's getUser() did — a deliberate relaxation. The answer only
 * drives cosmetic greetings (which CTA to show, "Signed in as …"), never
 * authorization: every gated page still validates server-side. Trading that
 * validation away is what lets the landing page render statically.
 *
 * The first render always reports signed-out, matching the static HTML, so
 * hydration never mismatches; the signed-in state arrives a beat later.
 */
export function useBrowserSession(): {
  signedIn: boolean;
  email: string | null;
} {
  const [signedIn, setSignedIn] = useState(false);
  const [email, setEmail] = useState<string | null>(null);

  useEffect(() => {
    const supabase = createClient();
    let cancelled = false;

    supabase.auth
      .getSession()
      .then(({ data }) => {
        if (cancelled) return;
        setSignedIn(Boolean(data.session));
        setEmail(data.session?.user.email ?? null);
      })
      .catch((error) => {
        // Storage access can fail in embedded/private browsing contexts. The
        // signed-out default is already rendered and correct, so log and move on.
        logger.warn('Browser session read failed; staying signed-out', {
          error: error instanceof Error ? error.message : String(error),
        });
      });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, session) => {
      // A null session here means sign-out: revert to the signed-out state.
      setSignedIn(Boolean(session));
      setEmail(session?.user.email ?? null);
    });

    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }, []);

  return { signedIn, email };
}
