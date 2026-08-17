import Link from 'next/link';

import { Separator } from '@/components/ui/separator';
import {
  DEFINITION,
  FAQ,
  SPECIMEN,
  type SpecimenRow,
} from '@/lib/content/landing';
import { MARKETING_ROUTES } from '@/lib/routes';
import { ThemeToggle } from '../ui/theme-toggle';
import { HeaderCta, HeroCta } from './HeroCta';
import { FloatingFeedbackButton } from './ReportBug-Btn';
import { ServiceUnavailableBanner } from './ServiceUnavailableBanner';

const isServiceAvailable = process.env.NEXT_PUBLIC_SERVICE_AVAILABLE === 'true';

/**
 * The front door.
 *
 * With no users there is no honest social proof — no testimonials, no logos, no
 * counts — so the hero specimen does the persuading instead. It shows the actual
 * transformation on three real tracks, including one that has no clean version
 * and is therefore left out. That omission is the single most misunderstood thing
 * about the product, so it appears above the fold rather than buried in the FAQ.
 *
 * A server component with no props: everything crawlable renders statically,
 * and the only signed-in differences (header CTA, hero block) live in the
 * HeroCta client island.
 */
export default function LandingPage() {
  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border">
        <nav className="mx-auto flex max-w-5xl items-center justify-between px-6 py-5">
          <span className="font-display text-xl font-semibold text-foreground">
            RadioWash
          </span>
          <div className="flex items-center gap-4">
            <Link
              href={MARKETING_ROUTES.howItWorks}
              className="text-sm font-medium text-muted-foreground hover:text-foreground"
            >
              How it works
            </Link>
            <HeaderCta />
            <ThemeToggle />
          </div>
        </nav>
      </header>

      {!isServiceAvailable && (
        <div className="mx-auto max-w-5xl px-6">
          <ServiceUnavailableBanner />
        </div>
      )}

      <main className="mx-auto max-w-5xl px-6">
        <section className="py-16 sm:py-24">
          <p className="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
            For Apple Music
          </p>

          <h1 className="mt-5 max-w-2xl font-display text-4xl font-semibold leading-[1.1] text-foreground sm:text-5xl">
            The same playlist. None of the explicit versions.
          </h1>

          <p className="mt-6 max-w-xl text-lg text-muted-foreground">
            {DEFINITION}
          </p>

          <HeroCta serviceAvailable={isServiceAvailable} />

          <Specimen />
        </section>

        <Separator />

        <section className="py-16">
          <h2 className="font-display text-2xl font-semibold text-foreground">
            What it costs
          </h2>
          <div className="mt-8 grid gap-6 sm:grid-cols-2">
            <PriceCard
              price="$0"
              title="Cleaning playlists"
              body="Cleaning playlists is free and stays free. No trial, no track limit, no credit card."
            />
            <PriceCard
              price="$5/mo"
              title="Auto-Sync"
              body="When a source playlist changes, its clean copy follows. The only paid thing in the product."
            />
          </div>
        </section>

        <Separator />

        <section id="faq" className="py-16">
          <h2 className="font-display text-2xl font-semibold text-foreground">
            Questions
          </h2>
          <dl className="mt-8 space-y-8">
            {FAQ.map((item) => (
              <div key={item.question} className="max-w-2xl">
                <dt className="font-display text-base font-semibold text-foreground">
                  {item.question}
                </dt>
                <dd className="mt-2 text-muted-foreground">
                  {item.answer}
                  {item.more && (
                    <>
                      {' '}
                      <Link
                        href={item.more.href}
                        className="whitespace-nowrap text-foreground underline underline-offset-4 hover:text-primary"
                      >
                        {item.more.label}
                      </Link>
                    </>
                  )}
                </dd>
              </div>
            ))}
          </dl>
        </section>
      </main>

      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-5xl flex-col gap-3 px-6 py-8 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
          <span>Clean copies of Apple Music playlists.</span>
          <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
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
            <Link href="/privacy" className="hover:text-foreground">
              Privacy
            </Link>
            <Link href="/terms" className="hover:text-foreground">
              Terms
            </Link>
            <span>Not affiliated with Apple.</span>
          </div>
        </div>
      </footer>

      <FloatingFeedbackButton />
    </div>
  );
}

function Specimen() {
  return (
    <figure className="mt-14 overflow-hidden rounded-md border border-border bg-card">
      <table className="w-full text-left text-sm">
        <caption className="sr-only">
          Three example tracks and what happens to each in a clean copy
        </caption>
        <thead>
          <tr className="border-b border-border text-xs uppercase tracking-wide text-muted-foreground">
            <th scope="col" className="px-4 py-3 font-medium sm:px-6">
              In your playlist
            </th>
            <th scope="col" className="px-4 py-3 font-medium sm:px-6">
              In the clean copy
            </th>
          </tr>
        </thead>
        <tbody>
          {SPECIMEN.map((row) => (
            <SpecimenRowView key={row.title} row={row} />
          ))}
        </tbody>
      </table>

      <figcaption className="border-t border-border px-4 py-4 text-sm text-muted-foreground sm:px-6">
        No clean version of <span className="text-foreground">rockstar</span>{' '}
        exists, so it&apos;s left out. A clean copy is sometimes shorter than
        its source — that&apos;s the point: everything in it is actually clean.
      </figcaption>
    </figure>
  );
}

function SpecimenRowView({ row }: { row: SpecimenRow }) {
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-4 align-top sm:px-6">
        <span className="text-foreground">{row.title}</span>
        {row.explicit && (
          <span
            className="ml-2 rounded-sm border border-border px-1 text-[10px] font-medium uppercase text-muted-foreground"
            title="Explicit"
          >
            E
          </span>
        )}
        <span className="mt-0.5 block text-muted-foreground">{row.artist}</span>
      </td>
      <td className="px-4 py-4 align-top sm:px-6">
        <span
          className={
            row.outcome === 'omitted'
              ? 'text-muted-foreground'
              : 'text-foreground'
          }
        >
          {row.result}
        </span>
        <span className="mt-0.5 block text-muted-foreground">
          {row.outcome === 'cleaned' && 'Cleaned'}
          {row.outcome === 'already-clean' && 'Already clean'}
          {row.outcome === 'omitted' && 'No clean version exists'}
        </span>
      </td>
    </tr>
  );
}

function PriceCard({
  price,
  title,
  body,
}: {
  price: string;
  title: string;
  body: string;
}) {
  return (
    <div className="rounded-md border border-border bg-card p-6">
      <p className="font-display text-3xl font-semibold tabular text-foreground">
        {price}
      </p>
      <h3 className="mt-2 font-display text-base font-semibold text-foreground">
        {title}
      </h3>
      <p className="mt-2 text-sm text-muted-foreground">{body}</p>
    </div>
  );
}
