import { MetadataRoute } from 'next';

export default function sitemap(): MetadataRoute.Sitemap {
  const baseUrl = 'https://radiowash.com';

  return [
    {
      url: baseUrl,
      lastModified: '2026-08-07',
      changeFrequency: 'weekly',
      priority: 1,
    },
    {
      url: `${baseUrl}/privacy`,
      lastModified: '2026-08-07',
      changeFrequency: 'yearly',
      priority: 0.3,
    },
    {
      url: `${baseUrl}/terms`,
      lastModified: '2026-08-07',
      changeFrequency: 'yearly',
      priority: 0.3,
    },
  ];
}
