/**
 * Landing page copy that appears in more than one place.
 *
 * The FAQ was previously written twice — once as visible markup in LandingPage
 * and once as FAQPage JSON-LD in StructuredData — so the two could drift and
 * search results could quote answers the page no longer gave. Both now read from
 * here.
 */

import { MARKETING_ROUTES } from '../routes';

export type FaqItem = {
  question: string;
  answer: string;
  /**
   * Optional trailing link rendered after the visible answer. Deliberately not
   * part of `answer` itself: the FAQPage JSON-LD consumes `answer` as plain
   * text, and a link belongs on the page, not in the schema.
   */
  more?: { label: string; href: string };
};

/**
 * One sentence that says what RadioWash is. Rendered under the landing H1 and
 * reused wherever a plain definition is needed, so the claim never drifts.
 */
export const DEFINITION =
  'RadioWash makes a clean copy of any Apple Music playlist — the same songs with radio edits swapped in where they exist. Your original playlist is never changed, and cleaning is free.';

export const FAQ: FaqItem[] = [
  {
    question: 'Does RadioWash change my original playlist?',
    answer:
      'No. RadioWash only ever creates a new playlist in your library. Your original is never edited, reordered, or deleted.',
    more: { label: 'See how it works', href: MARKETING_ROUTES.howItWorks },
  },
  {
    question: 'Why is my clean copy shorter than the original?',
    answer:
      "Some explicit tracks have no clean version — the artist never released one. Those tracks are left out rather than swapped for something that isn't the same song, so the copy contains only non-explicit material.",
    more: {
      label: 'See how matching works',
      href: MARKETING_ROUTES.howItWorks,
    },
  },
  {
    question: 'Do I need an Apple Music subscription?',
    answer:
      'Yes. RadioWash works inside your own Apple Music library, which requires an active subscription. There is no way around this.',
  },
  {
    question: 'What does it cost?',
    answer:
      'Cleaning playlists is free, with no track limit and no credit card. Auto-Sync — which keeps a clean copy in step with its source — is $5 per month and is the only paid part of the product.',
  },
];

/**
 * The hero specimen. Three real rows standing in for social proof, which a
 * product with no users cannot honestly show.
 *
 * The third row is the important one: it teaches, above the fold, that a clean
 * copy is sometimes shorter than its source. Users who expect a 1:1 copy
 * otherwise read the difference as a bug.
 */
export const SPECIMEN = [
  {
    title: 'Without Me',
    artist: 'Eminem',
    explicit: true,
    result: 'Without Me (Clean)',
    outcome: 'cleaned',
  },
  {
    title: 'Mr. Brightside',
    artist: 'The Killers',
    explicit: false,
    result: 'Unchanged',
    outcome: 'already-clean',
  },
  {
    title: 'rockstar',
    artist: 'Post Malone, 21 Savage',
    explicit: true,
    result: 'Not in the copy',
    outcome: 'omitted',
  },
] as const;

export type SpecimenRow = (typeof SPECIMEN)[number];
