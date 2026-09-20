import type { FaqItem } from './landing';

/**
 * FAQ for the /apple-music-clean-playlist landing page.
 *
 * Deliberately shares no questions with the homepage FAQ: two pages emitting
 * the same FAQPage schema compete with each other, and someone who arrived
 * searching for a tool has different questions than someone who landed cold.
 *
 * Every answer here is checked against the API. The timing answer stays
 * qualitative on purpose — a clean job prefetches exact-recording (ISRC)
 * matches in batches, falls back to a catalog search per unmatched track, and
 * backs off when Apple rate-limits (AppleMusicService.cs), so wall time
 * depends on the playlist and the catalog, not on a number we can quote.
 */
export const LANDING_FAQ: FaqItem[] = [
  {
    question: 'Does it work on playlists I did not create?',
    answer:
      'RadioWash reads the playlists in your own Apple Music library, so anything saved there can be cleaned — including playlists you added from someone else. The clean copy is always created as a new playlist in your library.',
  },
  {
    question: 'How long does a large playlist take?',
    answer:
      'It runs as a background job, so you can close the page and come back. Longer playlists take longer: RadioWash looks up exact recording matches in batches, searches the Apple Music catalog for each track that still needs one, and waits when Apple asks it to slow down. Progress updates as it goes, and the job page shows how every track was matched.',
  },
  {
    question: 'What happens to songs with no clean version?',
    answer:
      'They are left out of the copy. Some songs were never released as a radio edit, and RadioWash will not substitute a cover, a remix, or a different recording in their place — so a clean copy is sometimes shorter than its source.',
  },
  {
    question: 'Do I have to pay?',
    answer:
      'Cleaning playlists is free, with no track limit and no credit card. Auto-Sync, which keeps a clean copy in step with its source as the source changes, is $5 per month and is the only paid part of RadioWash.',
  },
];
