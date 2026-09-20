import type { Metadata } from 'next';
import Link from 'next/link';

import { CtaLink } from '@/components/ui/cta-link';
import {
  StructuredData,
  faqPageSchema,
  softwareApplicationSchema,
  type JsonLdSchema,
} from '@/components/StructuredData';
import { LANDING_FAQ } from '@/lib/content/apple-music-clean-playlist';
import { SPECIMEN } from '@/lib/content/landing';
import { pageOpenGraph } from '@/lib/metadata';
import { MARKETING_ROUTES } from '@/lib/routes';

export const metadata: Metadata = {
  title: 'Clean playlist app for Apple Music',
  description:
    'Make a clean copy of any Apple Music playlist. Explicit tracks are replaced with their radio edits where one exists; anything without a clean version is left out. Free, and your original playlist is never changed.',
  alternates: { canonical: './' },
  // Per-page, or this inherits the root layout's og:url and advertises the
  // homepage as its own social URL.
  openGraph: pageOpenGraph(),
};

// This page's own schema. The application entity is the shared one, so this
// page and the homepage describe a single SoftwareApplication under one @id
// rather than two competing definitions; only the FAQ is page-specific.
const PAGE_SCHEMAS: JsonLdSchema[] = [
  softwareApplicationSchema,
  faqPageSchema(LANDING_FAQ),
];

export default function AppleMusicCleanPlaylistPage() {
  return (
    <>
      <StructuredData schemas={PAGE_SCHEMAS} />
      <article className="space-y-8">
        <header className="space-y-4">
          <h1 className="font-display text-3xl font-semibold text-foreground">
            A clean playlist app for Apple Music
          </h1>
          <p className="text-muted-foreground">
            RadioWash makes a clean copy of an Apple Music playlist: the same
            songs, with each explicit track replaced by the radio edit the
            artist released. It is not a filter that hides songs — it swaps in
            the clean recording where one exists, and leaves the track out where
            none does. Your original playlist is never changed.
          </p>
        </header>

        <section className="space-y-3">
          <h2 className="font-display text-xl font-semibold text-foreground">
            What you get
          </h2>
          <p className="text-muted-foreground">
            A new playlist in your Apple Music library, holding the same songs
            in the same order, with three possible outcomes per track:
          </p>
          <dl className="space-y-3">
            {SPECIMEN.map((row) => (
              <div
                key={row.title}
                className="flex flex-wrap items-baseline gap-x-3 border-b border-border pb-3 last:border-b-0"
              >
                <dt className="font-medium text-foreground">
                  {row.title}
                  <span className="ml-2 font-normal text-muted-foreground">
                    {row.artist}
                  </span>
                </dt>
                <dd className="text-sm text-muted-foreground">
                  {row.outcome === 'cleaned' && (
                    <>Explicit, so it becomes {row.result}.</>
                  )}
                  {row.outcome === 'already-clean' && (
                    <>Already clean, so it carries over unchanged.</>
                  )}
                  {row.outcome === 'omitted' && (
                    <>No clean release exists, so it is left out.</>
                  )}
                </dd>
              </div>
            ))}
          </dl>
        </section>

        <section className="space-y-3">
          <h2 className="font-display text-xl font-semibold text-foreground">
            Replacing, not just filtering
          </h2>
          <p className="text-muted-foreground">
            Apple Music&apos;s own content restriction hides explicit songs:
            turn it on and the track disappears from the playlist, leaving a
            gap. RadioWash does something different. A clean version is a
            separate release with its own entry in the catalog — usually the
            radio edit — and RadioWash finds that release and puts it in the
            copy, so the song is still there.
          </p>
          <p className="text-muted-foreground">
            Matching the right recording is the hard part. RadioWash matches on
            the recording identifier first, then falls back to a catalog search
            that checks title, artist, and length before accepting a candidate.{' '}
            <Link
              href={MARKETING_ROUTES.howItWorks}
              className="underline underline-offset-4 hover:text-foreground"
            >
              How it works
            </Link>{' '}
            walks through that pipeline step by step.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="font-display text-xl font-semibold text-foreground">
            When there is no clean version
          </h2>
          <p className="text-muted-foreground">
            Not every explicit song has a clean counterpart — plenty were never
            released as a radio edit. Those tracks are left out of the copy
            rather than swapped for a cover, a remix, or a different song. That
            is why a clean copy is sometimes shorter than the playlist it came
            from, and the job page shows exactly which tracks were cleaned,
            which were already clean, and which were omitted.
          </p>
          <p className="text-muted-foreground">
            If you would rather do it by hand, or want to see what the manual
            route involves before signing in, the{' '}
            <Link
              href={MARKETING_ROUTES.cleanPlaylistGuide}
              className="underline underline-offset-4 hover:text-foreground"
            >
              clean-playlist guide
            </Link>{' '}
            covers both methods honestly.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="font-display text-xl font-semibold text-foreground">
            What it costs
          </h2>
          <p className="text-muted-foreground">
            Cleaning playlists is free, with no track limit and no credit card.
            Auto-Sync — which watches a source playlist and adds the clean
            versions of new songs to the copy — is $5 per month and is the only
            paid part of RadioWash.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="font-display text-xl font-semibold text-foreground">
            Questions
          </h2>
          <dl className="space-y-4">
            {LANDING_FAQ.map((item) => (
              <div key={item.question} className="space-y-1">
                <dt className="font-medium text-foreground">{item.question}</dt>
                <dd className="text-muted-foreground">{item.answer}</dd>
              </div>
            ))}
          </dl>
        </section>

        <section className="space-y-4 border-t border-border pt-8">
          <p className="text-muted-foreground">
            RadioWash works inside your own library, so it needs an active Apple
            Music subscription.
          </p>
          <CtaLink href="/auth">Make a clean copy — free</CtaLink>
        </section>
      </article>
    </>
  );
}
