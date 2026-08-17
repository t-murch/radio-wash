/**
 * The marketing/public routes, in one place.
 *
 * These URLs appear in the sitemap, two footers, the landing nav, and FAQ
 * answers. Nothing else keeps those in sync, so they all read from here.
 */
export const MARKETING_ROUTES = {
  home: '/',
  howItWorks: '/how-it-works',
  cleanPlaylistGuide: '/guides/clean-apple-music-playlist',
  privacy: '/privacy',
  terms: '/terms',
} as const;
