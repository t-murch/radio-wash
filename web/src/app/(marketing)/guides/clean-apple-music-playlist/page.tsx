import type { Metadata } from 'next';
import Link from 'next/link';

import { MARKETING_ROUTES } from '@/lib/routes';

export const metadata: Metadata = {
  title: 'How to make an Apple Music playlist clean',
  description:
    'Two honest ways to get a clean version of an Apple Music playlist: swapping tracks by hand in the Music app, or making a clean copy with RadioWash.',
  alternates: { canonical: './' },
};

export default function CleanPlaylistGuidePage() {
  return (
    <article className="space-y-8">
      <header className="space-y-4">
        <h1 className="font-display text-3xl font-semibold text-foreground">
          How to make an Apple Music playlist clean
        </h1>
        <p className="text-muted-foreground">
          Apple Music has no one-tap way to clean a playlist. There are two
          honest routes: swap the explicit tracks by hand in the Music app, or
          let RadioWash build a clean copy for you. This guide covers both, and
          when each one makes sense.
        </p>
      </header>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          What &ldquo;clean&rdquo; means on Apple Music
        </h2>
        <p className="text-muted-foreground">
          Apple marks explicit tracks with a small{' '}
          <span
            className="rounded-sm border border-border px-1 text-[10px] font-medium uppercase text-muted-foreground"
            title="Explicit"
          >
            E
          </span>{' '}
          badge. A clean version is not a censored playback mode — it is a
          separate release, usually the radio edit, with its own entry in the
          catalog. That has two consequences worth knowing before you start:
          the clean version has to be found and added like any other song, and
          not every explicit song has one. When the artist never released a
          radio edit, there is nothing clean to swap in.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          The manual way, in the Music app
        </h2>
        <ol className="list-decimal space-y-2 pl-5 text-muted-foreground">
          <li>
            Create a new, empty playlist. Working on a copy means your original
            stays intact if you change your mind.
          </li>
          <li>
            Go through the source playlist and add every track that has no{' '}
            <span
              className="rounded-sm border border-border px-1 text-[10px] font-medium uppercase text-muted-foreground"
              title="Explicit"
            >
              E
            </span>{' '}
            badge — those are already clean and can come over as they are.
          </li>
          <li>
            For each explicit track, search Apple Music for the song title. Scan
            the results for a version without the badge — it often lives on the
            album&apos;s clean edition or on a single, and the artwork can look
            identical, so check the badge rather than the cover.
          </li>
          <li>
            Compare the track length before adding. A version that runs
            noticeably longer or shorter is a different edit — a live cut or an
            extended mix — not the clean version of the song you have.
          </li>
          <li>
            When no clean version exists, skip the song. Leaving it out is the
            only way to keep the playlist actually clean.
          </li>
        </ol>
        <p className="text-muted-foreground">
          This works, and for a short playlist it can be done in a few minutes.
          The cost is that it is entirely per-track: there is no bulk operation,
          clean versions are easy to miss in search results, and the time scales
          with the length of the playlist. A long playlist means an afternoon of
          searching and badge-checking.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          The faster way: a clean copy with RadioWash
        </h2>
        <p className="text-muted-foreground">
          RadioWash does the same work automatically. It reads a playlist you
          pick, matches each track to its clean version — by exact recording
          identifier first, by careful search second — and writes the result
          back to your library as a new playlist. Your original is never
          touched, and tracks with no clean version are left out rather than
          replaced with something that isn&apos;t the same song. Cleaning is
          free, with no track limit and no credit card.
        </p>
        <p className="text-muted-foreground">
          The{' '}
          <Link
            href={MARKETING_ROUTES.howItWorks}
            className="text-foreground underline underline-offset-4 hover:text-primary"
          >
            how-it-works page
          </Link>{' '}
          explains the matching in detail, including exactly when a track is
          omitted.
        </p>
      </section>

      <section className="space-y-3">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Either way, know this
        </h2>
        <ul className="list-disc space-y-2 pl-5 text-muted-foreground">
          <li>
            Your original playlist never has to change. Build the clean version
            as a separate playlist, whichever route you take.
          </li>
          <li>
            Some songs have no clean edit at all. A clean playlist that is
            honest about that will sometimes be shorter than its source.
          </li>
          <li>
            Both approaches need an active Apple Music subscription — the
            clean versions live in Apple&apos;s catalog, and adding them to a
            library requires one.
          </li>
        </ul>
      </section>

      <section className="space-y-4 border-t border-border pt-8">
        <Link
          href="/auth"
          className="inline-flex items-center rounded-md bg-primary px-5 py-2.5 text-sm font-medium text-primary-foreground transition-colors hover:bg-brand-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
        >
          Make a clean copy — free
        </Link>
      </section>
    </article>
  );
}
