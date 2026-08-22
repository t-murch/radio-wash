import { describe, it, expect } from 'vitest';

import sitemap from '../sitemap';
import { MARKETING_ROUTES } from '@/lib/routes';

describe('sitemap', () => {
  it('lists every marketing page', () => {
    const urls = sitemap().map((entry) => entry.url);

    expect(urls).toEqual([
      'https://radiowash.com',
      `https://radiowash.com${MARKETING_ROUTES.howItWorks}`,
      `https://radiowash.com${MARKETING_ROUTES.cleanPlaylistGuide}`,
      `https://radiowash.com${MARKETING_ROUTES.privacy}`,
      `https://radiowash.com${MARKETING_ROUTES.terms}`,
    ]);
  });
});
