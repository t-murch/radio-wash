import { FAQ } from '@/lib/content/landing';

const SITE_URL = 'https://radiowash.com';

/**
 * Schema.org metadata for search results.
 *
 * The FAQ entries come from the same source as the visible page. They used to be
 * hand-copied here, which meant search results could quote answers the page no
 * longer gave.
 */
export function StructuredData() {
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
    // Next serves the generated app icon at /icon (no extension); /icon.png 404s.
    logo: `${SITE_URL}/icon`,
    description:
      'Makes clean copies of Apple Music playlists, substituting radio edits for explicit tracks.',
    foundingDate: '2024',
    sameAs: [],
  };

  const softwareApplicationSchema = {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: 'RadioWash',
    applicationCategory: 'MultimediaApplication',
    operatingSystem: 'Web',
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

  const faqSchema = {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: FAQ.map((item) => ({
      '@type': 'Question',
      name: item.question,
      acceptedAnswer: {
        '@type': 'Answer',
        text: item.answer,
      },
    })),
  };

  return (
    <>
      {[
        websiteSchema,
        organizationSchema,
        softwareApplicationSchema,
        faqSchema,
      ].map((schema, i) => (
        <script
          key={i}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(schema) }}
        />
      ))}
    </>
  );
}
