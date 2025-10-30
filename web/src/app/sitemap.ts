import { MetadataRoute } from 'next';

export default function sitemap(): MetadataRoute.Sitemap {
  const baseUrl = 'https://radiowash.com';

  return [
    {
      url: baseUrl,
      lastModified: '2025-01-30',
      changeFrequency: 'weekly',
      priority: 1,
    },
  ];
}
