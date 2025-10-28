export function StructuredData() {
  const websiteSchema = {
    '@context': 'https://schema.org',
    '@type': 'WebSite',
    name: 'RadioWash',
    url: 'https://radiowash.com',
    description:
      'Transform explicit Spotify playlists into clean versions with AI-powered matching',
    potentialAction: {
      '@type': 'SearchAction',
      target: 'https://radiowash.com/auth',
      'query-input': 'required name=search_term_string',
    },
  };

  const organizationSchema = {
    '@context': 'https://schema.org',
    '@type': 'Organization',
    name: 'RadioWash',
    url: 'https://radiowash.com',
    logo: 'https://radiowash.com/icon.png',
    description:
      'AI-powered tool to clean explicit content from Spotify playlists',
    foundingDate: '2024',
    sameAs: [],
  };

  const softwareApplicationSchema = {
    '@context': 'https://schema.org',
    '@type': 'SoftwareApplication',
    name: 'RadioWash',
    applicationCategory: 'MultimediaApplication',
    operatingSystem: 'Web',
    offers: {
      '@type': 'Offer',
      price: '0',
      priceCurrency: 'USD',
    },
    aggregateRating: {
      '@type': 'AggregateRating',
      ratingValue: '4.8',
      ratingCount: '1000',
    },
    description:
      'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks with an 80%+ success rate.',
    featureList: [
      'AI-Powered Matching',
      'Lightning Fast Processing',
      'Secure & Private',
      'High Success Rate (80%+)',
      'Preserves Quality',
      'Completely Free',
    ],
  };

  const faqSchema = {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: [
      {
        '@type': 'Question',
        name: 'How does RadioWash find clean alternatives?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: "We use advanced matching algorithms to search Spotify's catalog for clean versions of the same songs by the same artists, or similar tracks with clean lyrics.",
        },
      },
      {
        '@type': 'Question',
        name: 'Is my Spotify data safe?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Absolutely. We only access your playlists to read track information and create new clean playlists. We never store your personal data or modify your existing playlists.',
        },
      },
      {
        '@type': 'Question',
        name: 'What if no clean version exists?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: "If we can't find a clean alternative, the track simply won't be included in the new playlist. You'll see a detailed report of what was and wasn't matched.",
        },
      },
      {
        '@type': 'Question',
        name: 'Does this work with all Spotify accounts?',
        acceptedAnswer: {
          '@type': 'Answer',
          text: 'Yes! RadioWash works with both free and premium Spotify accounts.',
        },
      },
    ],
  };

  return (
    <>
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(websiteSchema) }}
      />
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(organizationSchema) }}
      />
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{
          __html: JSON.stringify(softwareApplicationSchema),
        }}
      />
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(faqSchema) }}
      />
    </>
  );
}
