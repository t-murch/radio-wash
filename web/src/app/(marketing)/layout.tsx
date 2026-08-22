import Link from 'next/link';

import { MARKETING_ROUTES } from '@/lib/routes';

/**
 * Shared shell for the public content pages (/how-it-works, /guides/*).
 * Same idea as the (legal) shell: read signed-out, so a static wordmark and no
 * Supabase client. Kept separate because the marketing footer links a fuller
 * set of pages than the legal one.
 */
export default function MarketingLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border bg-card">
        <div className="mx-auto max-w-2xl px-4 py-3 sm:px-6">
          <Link
            href={MARKETING_ROUTES.home}
            className="font-display text-xl font-semibold text-foreground"
          >
            RadioWash
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-2xl px-4 py-12 sm:px-6">{children}</main>
      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-2xl flex-wrap gap-x-6 gap-y-2 px-4 py-8 text-sm text-muted-foreground sm:px-6">
          <Link href={MARKETING_ROUTES.home} className="hover:text-foreground">
            Home
          </Link>
          <Link
            href={MARKETING_ROUTES.howItWorks}
            className="hover:text-foreground"
          >
            How it works
          </Link>
          <Link
            href={MARKETING_ROUTES.cleanPlaylistGuide}
            className="hover:text-foreground"
          >
            Clean-playlist guide
          </Link>
          <Link
            href={MARKETING_ROUTES.privacy}
            className="hover:text-foreground"
          >
            Privacy
          </Link>
          <Link href={MARKETING_ROUTES.terms} className="hover:text-foreground">
            Terms
          </Link>
        </div>
      </footer>
    </div>
  );
}
