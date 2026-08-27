/**
 * Shown by a route-level SentryErrorBoundary when a client component crashes in
 * the commit phase — most often a browser extension or Chrome's page translation
 * mutating the DOM out from under React (see Sentry JAVASCRIPT-NEXTJS-21/-22).
 * A plain anchor, not router navigation: the React tree may be broken, and a
 * full reload is the recovery.
 */
export function RouteErrorFallback({ retryHref }: { retryHref: string }) {
  return (
    <div className="w-full max-w-md space-y-4">
      <h1 className="font-display text-2xl font-semibold text-foreground">
        Something went wrong
      </h1>
      <p className="text-muted-foreground">
        <a
          href={retryHref}
          className="text-primary underline underline-offset-4"
        >
          Reload this page
        </a>{' '}
        to carry on.
      </p>
    </div>
  );
}
