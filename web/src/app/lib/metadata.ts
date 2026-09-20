import type { Metadata } from 'next';

/**
 * The Open Graph fields every page shares. Next replaces a page's `openGraph`
 * wholesale instead of merging it with the root layout's, so a page that only
 * wants its own og:url would otherwise drop og:site_name, og:type and
 * og:locale. Pages spread this in and add what is theirs.
 */
export const SITE_OPEN_GRAPH = {
  type: 'website',
  locale: 'en_US',
  siteName: 'RadioWash',
} as const satisfies Metadata['openGraph'];

/**
 * Open Graph for a marketing page that advertises itself as its own social
 * URL. `url` is relative, like `alternates.canonical`; both resolve against
 * `metadataBase`. Title and description are back-filled by Next from the
 * page's own metadata.
 */
export function pageOpenGraph(): NonNullable<Metadata['openGraph']> {
  return { ...SITE_OPEN_GRAPH, url: './' };
}
