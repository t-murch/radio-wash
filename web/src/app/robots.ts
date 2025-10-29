import { MetadataRoute } from 'next';

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: '*',
        allow: '/',
        disallow: ['/dashboard', '/subscription', '/jobs', '/api'],
      },
    ],
    sitemap: 'https://radiowash.com/sitemap.xml',
  };
}
