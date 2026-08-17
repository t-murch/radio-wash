import Link from 'next/link';

/**
 * Shared shell for /privacy and /terms. Deliberately not GlobalHeader: these
 * pages are read signed-out from the landing footer, and a static wordmark
 * needs no Supabase client.
 */
export default function LegalLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border bg-card">
        <div className="mx-auto max-w-2xl px-4 py-3 sm:px-6">
          <Link
            href="/"
            className="font-display text-xl font-semibold text-foreground"
          >
            RadioWash
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-2xl px-4 py-12 sm:px-6">{children}</main>
      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-2xl gap-6 px-4 py-8 text-sm text-muted-foreground sm:px-6">
          <Link href="/" className="hover:text-foreground">
            Home
          </Link>
          <Link href="/how-it-works" className="hover:text-foreground">
            How it works
          </Link>
          <Link href="/privacy" className="hover:text-foreground">
            Privacy
          </Link>
          <Link href="/terms" className="hover:text-foreground">
            Terms
          </Link>
        </div>
      </footer>
    </div>
  );
}
