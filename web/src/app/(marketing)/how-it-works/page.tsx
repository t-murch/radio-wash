import type { Metadata } from 'next';

import { CtaLink } from '@/components/ui/cta-link';
import { DEFINITION } from '@/lib/content/landing';

export const metadata: Metadata = {
  title: 'How it works',
  description:
    'How RadioWash finds the clean version of every track: exact recording matches first, careful search second, and honest omission when no clean version exists.',
  alternates: { canonical: './' },
};

// The claims on this page map to the actual matching pipeline (TrackMatcher on
// the API side). If the matching behavior changes, this page changes in the
// same PR — same rule as the legal pages.
export default function HowItWorksPage() {
  return (
    <article className="space-y-8">
      <header className="space-y-4">
        <h1 className="font-display text-3xl font-semibold text-foreground">
          How RadioWash works
        </h1>
        <p className="text-muted-foreground">{DEFINITION}</p>
      </header>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What a clean copy is
        </h2>
        <p className="text-muted-foreground">
          A clean copy is a new playlist that RadioWash creates in your Apple
          Music library. It holds the same songs as the playlist you started
          from, with each explicit track replaced by its clean version — the
          radio edit the artist actually released. Your original playlist is
          never edited, reordered, or deleted; RadioWash only ever adds a new
          playlist next to it.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          How a clean match is found
        </h2>
        <p className="text-muted-foreground">
          For every track in your playlist, RadioWash works through the same
          steps, in order:
        </p>
        <ol className="list-decimal space-y-2 pl-5 text-muted-foreground">
          <li>
            <strong className="text-foreground">
              Match the exact recording.
            </strong>{' '}
            Every released recording carries an identifier (an ISRC) that pins
            down the exact version of a song. RadioWash matches on that first,
            and when the recording is explicit, it looks up the clean
            counterpart of that same recording.
          </li>
          <li>
            <strong className="text-foreground">
              Search, then check every candidate.
            </strong>{' '}
            When there is no identifier match, RadioWash searches the Apple
            Music catalog by song title and artist, and keeps only candidates
            whose title, artist, and length line up with the original — a
            version that runs several seconds longer or shorter is a different
            edit, not a match. Apple Music search offers no way to ask for
            non-explicit results only, so RadioWash checks the content rating of
            each candidate itself.
          </li>
          <li>
            <strong className="text-foreground">
              Leave the track out if nothing clean exists.
            </strong>{' '}
            Some songs simply have no clean release. Those tracks are omitted
            from the copy rather than swapped for a cover, a remix, or a
            different song. Nothing is ever substituted that isn&apos;t the same
            recording.
          </li>
        </ol>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Why a clean copy can be shorter
        </h2>
        <p className="text-muted-foreground">
          Because of that last step, a clean copy sometimes has fewer songs than
          its source. That is deliberate: the promise is that everything in the
          copy is actually clean, and the only honest way to keep it is to leave
          out what has no clean version. The job page shows exactly how every
          track matched, so you can see what was cleaned, what was already
          clean, and what was left out.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Auto-Sync
        </h2>
        <p className="text-muted-foreground">
          Playlists change. Auto-Sync watches a source playlist and, when new
          songs appear, adds their clean versions to the copy — so the copy
          keeps up without you re-running anything. Auto-Sync only adds:
          removing a song from the source does not remove it from the copy,
          because Apple&apos;s API offers no way to do that. Auto-Sync costs $5
          per month and is the only paid part of RadioWash. Cleaning playlists
          is free.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Reconnecting Apple Music
        </h2>
        <p className="text-muted-foreground">
          Apple&apos;s authorization for third-party apps expires periodically,
          by design. When that happens, RadioWash asks you to reconnect — a
          couple of clicks, not a bug, and nothing about your playlists or
          copies is lost.
        </p>
      </section>

      <section className="space-y-4 border-t border-border pt-8">
        <p className="text-muted-foreground">
          RadioWash works inside your own library, so it needs an active Apple
          Music subscription.
        </p>
        <CtaLink href="/auth">Make a clean copy — free</CtaLink>
      </section>
    </article>
  );
}
