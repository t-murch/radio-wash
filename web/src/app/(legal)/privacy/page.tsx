import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'Privacy',
  description:
    'What RadioWash stores, what it never sees, and the controls you have over both.',
};

// Legal copy is deliberately plain prose, structured so each claim maps to
// something the code actually does. If a feature changes what is stored,
// this page changes in the same PR.
export default function PrivacyPage() {
  return (
    <article className="space-y-8">
      <header className="space-y-2">
        <h1 className="font-display text-3xl font-semibold text-foreground">
          Privacy
        </h1>
        <p className="text-sm text-muted-foreground">
          Last updated August 7, 2026
        </p>
      </header>

      <section className="space-y-3">
        <p className="text-muted-foreground">
          RadioWash makes clean copies of Apple Music playlists. Doing that
          requires storing a small amount of data about you and your playlists.
          This page lists all of it — there is no data collection beyond what is
          described here.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What we store
        </h2>
        <ul className="list-disc space-y-2 pl-5 text-muted-foreground">
          <li>
            <strong className="text-foreground">Your account.</strong> The email
            address and name that come from how you sign in — an email magic
            link, Google, or Apple. We never see a password: sign-in is handled
            by Supabase, and the magic-link flow has no password at all.
          </li>
          <li>
            <strong className="text-foreground">
              Your Apple Music connection.
            </strong>{' '}
            When you connect Apple Music, Apple gives us a token that lets
            RadioWash read your playlists and create the clean copies in your
            library. We store that token and nothing else from your Apple
            account — not your Apple ID password, not your listening history.
          </li>
          <li>
            <strong className="text-foreground">Your jobs.</strong> For each
            clean copy you make, we keep the playlist names and the
            track-by-track record of what matched what, so the job page can show
            you exactly what happened.
          </li>
          <li>
            <strong className="text-foreground">Your subscription.</strong> If
            you subscribe to Auto-Sync, payment is handled entirely by Stripe.
            We store your subscription status and Stripe's identifiers for it.
            Your card number never touches our servers.
          </li>
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What we don&apos;t do
        </h2>
        <ul className="list-disc space-y-2 pl-5 text-muted-foreground">
          <li>No advertising, and no selling or sharing data with anyone.</li>
          <li>No third-party analytics or tracking scripts.</li>
          <li>
            No marketing email. The only email RadioWash sends is the sign-in
            link you ask for.
          </li>
        </ul>
        <p className="text-muted-foreground">
          One exception worth being explicit about: when something breaks, an
          error report goes to Sentry, our error-tracking service. That report
          can include your account id, email, and display name alongside the
          technical details, so we can tell whether a bug hit one person or
          everyone. It is used to fix bugs and for nothing else.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Cookies
        </h2>
        <p className="text-muted-foreground">
          RadioWash uses cookies only to keep you signed in. There are no
          advertising or analytics cookies, which is why there is no cookie
          banner.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Your controls
        </h2>
        <ul className="list-disc space-y-2 pl-5 text-muted-foreground">
          <li>
            <strong className="text-foreground">Disconnect Apple Music</strong>{' '}
            from the dashboard at any time. This deletes the stored token. To
            fully revoke RadioWash&apos;s access on Apple&apos;s side as well,
            remove it in your Apple account settings.
          </li>
          <li>
            <strong className="text-foreground">Cancel Auto-Sync</strong> from
            the subscription page. You keep access until the end of the period
            you paid for.
          </li>
          <li>
            <strong className="text-foreground">Delete your account</strong> and
            everything above by emailing{' '}
            <a
              href="mailto:support@radiowash.com"
              className="text-primary underline underline-offset-2"
            >
              support@radiowash.com
            </a>
            . Your clean copies live in your Apple Music library and stay yours;
            deletion removes what RadioWash stores.
          </li>
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Changes and contact
        </h2>
        <p className="text-muted-foreground">
          If this policy changes in a way that matters, the date at the top
          changes with it. Questions go to{' '}
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
