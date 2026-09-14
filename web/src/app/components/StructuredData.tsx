import { FAQ } from '@/lib/content/landing';

export type JsonLdSchema = Record<string, unknown>;

type FaqLike = { question: string; answer: string };

const SITE_URL = 'https://radiowash.com';

/**
 * Builds a FAQPage block from the same items a page renders, so the schema
 * cannot quote answers the page no longer gives.
 */
export function faqPageSchema(items: readonly FaqLike[]): JsonLdSchema {
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
 * Renders JSON-LD blocks. Called bare it emits the site-level schemas the
 * homepage has always carried; pass `schemas` to emit a page's own instead.
 */
export function StructuredData({ schemas }: { schemas?: JsonLdSchema[] }) {
  const websiteSchema = {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: 'RadioWash',
    url: SITE_URL,
    description:
      'Make a clean copy of any Apple Music playlist — same songs, radio edits substituted, your original untouched.',
  };

  const organizationSchema = {
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

  const softwareApplicationSchema = {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: 'RadioWash',
    applicationCategory: 'MultimediaApplication',
    operatingSystem: 'Web',
    // Two offers, because the free and paid parts are genuinely different things:
    // cleaning is free up to the plan's playlist cap, and Auto-Sync is the only
    // paid feature.
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
      'RadioWash creates clean copies of Apple Music playlists: the same songs with radio edits substituted where they exist. Tracks without a clean version are left out, so the copy contains only non-explicit material. The original playlist is never changed. Cleaning is free for your first 10 playlists; Auto-Sync ($5/month) keeps a copy in step with its source. Requires an active Apple Music subscription.',
  };

  const faqSchema = faqPageSchema(FAQ);

  const blocks = schemas ?? [
    websiteSchema,
    organizationSchema,
    softwareApplicationSchema,
    faqSchema,
  ];

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
