'use client';

import Link from 'next/link';

import { CtaLink } from '@/components/ui/cta-link';
import { useBrowserSession } from '@/hooks/useBrowserSession';
import { signOut } from '../../auth/actions';

/**
 * The two spots on the landing page that differ for a signed-in visitor: the
 * header CTA and the hero block. Both live in this client island so the rest
 * of the page — hero copy, specimen, pricing, FAQ — stays a server component
 * and the route stays static.
 *
 * The server always renders the signed-out state; a returning user sees it for
 * a paint before the session check swaps in the welcome-back state. Accepted
 * trade-off: crawlers always get the marketing page as cached HTML.
 */

export function HeaderCta() {
  const { signedIn } = useBrowserSession();

  return signedIn ? (
    <Link
      href="/dashboard"
      className="text-sm font-medium text-primary underline-offset-4 hover:underline"
    >
      Open dashboard
    </Link>
  ) : (
    <CtaLink href="/auth" size="sm">
      Get started
    </CtaLink>
  );
}

export function HeroCta({ serviceAvailable }: { serviceAvailable: boolean }) {
  const { signedIn, email } = useBrowserSession();

  if (signedIn) {
    return (
      <>
        <p className="mt-6 max-w-xl text-lg text-muted-foreground">
          Welcome back. Your playlists and clean copies are on the dashboard.
        </p>
        <div className="mt-8">
          <CtaLink href="/dashboard">Go to your dashboard</CtaLink>
        </div>
        {email && (
          <p className="mt-4 text-sm text-muted-foreground">
            Signed in as {email} ·{' '}
            {/* A server action, not a link: signing out has to clear the
                session cookie server-side, which a GET cannot do. */}
            <form action={signOut} className="inline">
              <button
                type="submit"
                className="underline underline-offset-4 hover:text-foreground"
              >
                sign out
              </button>
            </form>
          </p>
        )}
      </>
    );
  }

  return (
    <>
      <div className="mt-8">
        {serviceAvailable ? (
          <CtaLink href="/auth">Make a clean copy — free</CtaLink>
        ) : (
          <span
            aria-describedby="service-unavailable-banner"
            className="inline-flex cursor-not-allowed items-center rounded-md bg-primary px-5 py-2.5 text-sm font-medium text-primary-foreground opacity-45"
          >
            Make a clean copy — free
          </span>
        )}
      </div>
      <p className="mt-4 max-w-xl text-sm text-muted-foreground">
        Works inside your own library, so it needs an active Apple Music
        subscription.
      </p>
    </>
  );
}
