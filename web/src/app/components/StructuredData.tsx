import type { ManualStep } from '@/lib/content/clean-playlist-guide';
import { FAQ, type FaqItem } from '@/lib/content/landing';

export type JsonLdSchema = Record<string, unknown>;

const SITE_URL = 'https://radiowash.com';

/**
 * The product, as one entity.
 *
 * Both the homepage and the landing page describe the same application, so they
 * emit this same object under one @id rather than two near-identical anonymous
 * ones — otherwise search engines see two competing SoftwareApplications and
 * pick whichever they like. `url` stays the canonical product URL for the same
 * reason; a page that wants to name itself does that through its own canonical
 * and og:url, not by redefining the entity.
 */
export const SOFTWARE_APPLICATION_ID = `${SITE_URL}/#software`;

export const softwareApplicationSchema: JsonLdSchema = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  '@id': SOFTWARE_APPLICATION_ID,
  name: 'RadioWash',
  applicationCategory: 'MultimediaApplication',
  operatingSystem: 'Web',
  url: SITE_URL,
  // Two offers, because the free and paid parts are genuinely different things:
  // cleaning is free without limit, and Auto-Sync is the only paid feature.
  offers: [
    {
      '@type': 'Offer',
      name: 'Playlist cleaning',
      price: '0',
      priceCurrency: 'USD',
    },
    {
      '@type': 'Offer',
      name: 'Auto-Sync',
      price: '5.00',
      priceCurrency: 'USD',
    },
  ],
  description:
    'RadioWash creates clean copies of Apple Music playlists: the same songs with radio edits substituted where they exist. Tracks without a clean version are left out, so the copy contains only non-explicit material. The original playlist is never changed. Cleaning is free; Auto-Sync ($5/month) keeps a copy in step with its source. Requires an active Apple Music subscription.',
};

/**
 * Builds a FAQPage block from the same items a page renders, so the schema
 * cannot quote answers the page no longer gives.
 */
export function faqPageSchema(
  items: readonly Pick<FaqItem, 'question' | 'answer'>[]
): JsonLdSchema {
  return {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: items.map((item) => ({
      '@type': 'Question',
      name: item.question,
      acceptedAnswer: {
        '@type': 'Answer',
        text: item.answer,
      },
    })),
  };
}

/**
 * Builds a HowTo block from the same steps a page renders.
 *
 * Same rule as faqPageSchema: the caller passes the list it renders, so the
 * schema cannot describe steps the page no longer shows.
 */
export function howToSchema(input: {
  name: string;
  description: string;
  steps: readonly ManualStep[];
}): JsonLdSchema {
  return {
    '@context': 'https://schema.org',
    '@type': 'HowTo',
    name: input.name,
    description: input.description,
    step: input.steps.map((step, i) => ({
      '@type': 'HowToStep',
      position: i + 1,
      name: step.name,
      text: step.text,
    })),
  };
}

const websiteSchema: JsonLdSchema = {
  '@context': 'https://schema.org',
  '@type': 'WebSite',
  name: 'RadioWash',
  url: SITE_URL,
  description:
    'Make a clean copy of any Apple Music playlist — same songs, radio edits substituted, your original untouched.',
};

const organizationSchema: JsonLdSchema = {
  '@context': 'https://schema.org',
  '@type': 'Organization',
  name: 'RadioWash',
  url: SITE_URL,
  // Google requires Organization.logo to be at least 112×112; the brand mark
  // is 264×264 and served straight from public/, no route indirection.
  logo: `${SITE_URL}/logo_assets/radiowash-mark.png`,
  description:
    'Makes clean copies of Apple Music playlists, substituting radio edits for explicit tracks.',
  foundingDate: '2024',
  sameAs: ['https://tillumlabs.com', 'https://github.com/t-murch/'],
};

/** The site-level schemas the homepage has always carried. */
const SITE_SCHEMAS: JsonLdSchema[] = [
  websiteSchema,
  organizationSchema,
  softwareApplicationSchema,
  faqPageSchema(FAQ),
];

/**
 * Renders JSON-LD blocks. Called bare it emits the site-level schemas; pass
 * `schemas` to emit a page's own instead.
 */
export function StructuredData({ schemas }: { schemas?: JsonLdSchema[] }) {
  const blocks = schemas ?? SITE_SCHEMAS;

  return (
    <>
      {blocks.map((schema, i) => (
        <script
          key={i}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(schema) }}
        />
      ))}
    </>
  );
}
