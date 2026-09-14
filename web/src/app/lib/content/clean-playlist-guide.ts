/**
 * The manual-method steps on /guides/clean-apple-music-playlist.
 *
 * Single source for the rendered <ol> and the HowTo schema, so search results
 * can't describe steps the page no longer shows. `name` is the short label
 * schema wants; `text` is the full instruction the page renders.
 *
 * The "E" badge in step 2 is rendered as a styled element on the page, so that
 * step's `text` spells the badge out in words instead.
 */
export type ManualStep = { name: string; text: string };

export const MANUAL_STEPS: ManualStep[] = [
  {
    name: 'Create an empty playlist',
    text: 'Create a new, empty playlist. Working on a copy means your original stays intact if you change your mind.',
  },
  {
    name: 'Add the tracks that are already clean',
    text: 'Go through the source playlist and add every track with no explicit badge — those are already clean and can come over as they are.',
  },
  {
    name: 'Search for a clean version of each explicit track',
    text: "For each explicit track, search Apple Music for the song title. Scan the results for a version without the badge — it often lives on the album's clean edition or on a single, and the artwork can look identical, so check the badge rather than the cover.",
  },
  {
    name: 'Check the track length before adding',
    text: 'Compare the track length before adding. A version that runs noticeably longer or shorter is a different edit — a live cut or an extended mix — not the clean version of the song you have.',
  },
  {
    name: 'Skip songs with no clean version',
    text: 'When no clean version exists, skip the song. Leaving it out is the only way to keep the playlist actually clean.',
  },
];
