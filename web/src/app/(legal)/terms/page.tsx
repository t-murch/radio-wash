import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Terms — RadioWash',
  description:
    'The terms of using RadioWash: what the service does, what it costs, and what it honestly cannot promise.',
};

export default function TermsPage() {
  return (
    <article className="space-y-8">
      <header className="space-y-2">
        <h1 className="font-display text-3xl font-semibold text-foreground">
          Terms of Service
        </h1>
        <p className="text-sm text-muted-foreground">
          Last updated August 9, 2026
        </p>
      </header>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What RadioWash is
        </h2>
        <p className="text-muted-foreground">
          RadioWash makes clean copies of your Apple Music playlists: the same
          songs, with radio edits swapped in where they exist, written back to
          your library as a new playlist. Your original playlist is never
          modified. Using RadioWash requires an active Apple Music subscription
          — that requirement is Apple&apos;s, and there is no way around it.
        </p>
        <p className="text-muted-foreground">
          RadioWash is an independent service. It is not made by, affiliated
          with, or endorsed by Apple. Apple Music is a trademark of Apple Inc.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What it honestly can&apos;t promise
        </h2>
        <ul className="list-disc space-y-2 pl-5 text-muted-foreground">
          <li>
            A clean copy can be shorter than its source. When no clean version
            of a song exists in Apple&apos;s catalog, the song is left out
            rather than replaced with the explicit one.
          </li>
          <li>
            Matching is automatic and occasionally wrong. The job page shows you
            exactly how every track matched so you can check.
          </li>
          <li>
            Auto-Sync only adds. When new songs appear in a source playlist,
            their clean versions are added to the copy. Removing a song from the
            source does not remove it from the copy — Apple&apos;s API offers no
            way to do that.
          </li>
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Your account
        </h2>
        <p className="text-muted-foreground">
          You are responsible for the account you sign in with and for what
          happens under it. Don&apos;t abuse the service — automated bulk use
          and attempts to work around rate limits can get an account suspended.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Payment
        </h2>
        <p className="text-muted-foreground">
          Cleaning playlists is free. Auto-Sync is a paid subscription, billed
          monthly through Stripe at the price shown on the subscription page.
          You can cancel at any time; cancellation takes effect at the end of
          the period you already paid for, and you keep access until then.
          Payments already made are not refunded.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          The service, as provided
        </h2>
        <p className="text-muted-foreground">
          RadioWash is provided as-is, without warranties. It depends on
          Apple&apos;s APIs, which can change or break outside our control.
          Features may change and the service may be discontinued. Our total
          liability for anything arising from the service is limited to the
          amount you paid for it in the preceding twelve months.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Changes and contact
        </h2>
        <p className="text-muted-foreground">
          When these terms change in a way that matters, the date at the top
          changes with it, and continued use after that means acceptance.
          Questions go to{' '}
          <a
            href="mailto:support@radiowash.com"
            className="text-primary underline underline-offset-2"
          >
            support@radiowash.com
          </a>
          .
        </p>
      </section>
    </article>
  );
}
