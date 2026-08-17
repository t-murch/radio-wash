import { MetadataRoute } from 'next';

import { MARKETING_ROUTES } from './lib/routes';

export default function sitemap(): MetadataRoute.Sitemap {
  const baseUrl = 'https://radiowash.com';

  return [
    {
      url: baseUrl,
      lastModified: '2026-08-17',
      changeFrequency: 'weekly',
      priority: 1,
    },
    {
      url: `${baseUrl}${MARKETING_ROUTES.howItWorks}`,
      lastModified: '2026-08-17',
      changeFrequency: 'monthly',
      priority: 0.8,
    },
    {
      url: `${baseUrl}${MARKETING_ROUTES.cleanPlaylistGuide}`,
      lastModified: '2026-08-17',
      changeFrequency: 'monthly',
      priority: 0.7,
    },
    {
      url: `${baseUrl}/privacy`,
      lastModified: '2026-08-07',
      changeFrequency: 'yearly',
      priority: 0.3,
    },
    {
      url: `${baseUrl}/terms`,
      lastModified: '2026-08-09',
      changeFrequency: 'yearly',
      priority: 0.3,
    },
  ];
}
