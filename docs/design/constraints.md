# Apple Music constraints

What the Apple Music integration can and cannot do, and what each limit forces
in the interface. Sourced from `api/Services/Implementations/AppleMusicService.cs`,
`AppleMusicMusicService.cs`, and `web/src/app/lib/providers.ts`.

These are **permanent properties of Apple's API**, not temporary gaps. Design
around them rather than assuming they will be fixed.

---

## Hard constraints

### Library playlists report no track count

Apple's library playlist attributes omit the track count, so
`AppleMusicMusicService` reports `0` for every playlist in a list view.

> **Design:** a playlist card cannot display "142 tracks." Either omit the count
> entirely, or fetch it lazily per playlist and design a loading state for it.
> Do not design a card whose composition depends on that number being present.

### Library items have no public URL

Apple library IDs (`p.xxxxx` for playlists, `i.xxxxx` for tracks) address items
in *the user's own library*. They have no shareable web URL. Only catalog items
do.

> **Design:** there is no reliable "Open in Apple Music" button and no per-track
> link for library items. Do not design a card or table row with a link slot
> that will frequently be empty. `providerUrl()` already returns nothing for
> these, which is why those affordances vanish on Apple today.

### No token refresh

Apple issues a Music User Token valid for roughly six months. There is no
refresh flow — when it expires, the user must re-authorize through MusicKit.
The stored `ExpiresAt` is an *assumption* based on
`AppleMusicSettings.UserTokenAssumedLifetimeDays`, not something Apple confirms.

The app prompts for reconnection 14 days ahead of assumed expiry
(`APPLE_RECONNECT_WINDOW_DAYS` in `ProviderConnectionStatus.tsx`).

> **Design:** a recurring reconnect prompt is a permanent part of the product.
> It is **not** an error — design it as routine maintenance. See
> `screenshots/dashboard-apple-reconnect__light__desktop.png` for today's
> treatment, which reads as a warning.

### No user profile

Apple exposes no profile endpoint. `GetUserProfileAsync` returns a placeholder
(`apple_music:{storefront}`).

> **Design:** display name and avatar must come from the Supabase identity
> (Apple/Google sign-in), never from the music service. Do not design a
> "connected as @username" affordance for the music account.

### An Apple Music subscription is mandatory

MusicKit will not issue a Music User Token to a user without an active Apple
Music subscription. This is a hard block on the entire product.

> **Design:** this needs a real screen, not an error line. A user who signs in
> successfully and then cannot authorize music has hit a dead end today.

### Personal uploads and region-gapped tracks are unmatchable

Tracks with no Apple catalog ID (personal library uploads, regional
unavailability) cannot be matched or added to a playlist. They are silently
dropped when writing.

> **Design:** the job result needs to account for tracks that were skipped for
> reasons other than "no clean version exists." Today these are indistinguishable.

---

## Soft constraints (batching and rate limits)

These affect timing and progress design, not layout.

| Limit | Value |
|---|---|
| Search results per request | 25 max |
| ISRC lookup batch | 25 per request (batched — better than Spotify's one-search-per-ISRC) |
| Catalog ID chunk | 100 |
| Add-tracks chunk | 25 |
| Rate limiting | honors `Retry-After`, clamped to 60s |
| Copy job ISRC prefetch cap | 200 distinct ISRCs; overflow falls back to search matching |

> **Design:** jobs on large playlists take minutes, and the chunk sizes above
> mean progress advances in visible steps rather than smoothly. The `Processing`
> state deserves design attention proportional to how long users will look at it.

---

## Clean-version matching

How a clean counterpart is found, in order (`TrackMatcher.cs`):

1. **ISRC match** — exact recording identifier. If the match is explicit and a
   clean version is wanted, search for the clean counterpart (`isrc-clean`).
2. **Text search** — fall back to name/artist search, filtered by name, artist,
   and duration plausibility (3000ms tolerance) → `search` or `search-clean`.
3. **No match** — `none`.

Apple has no `-tag:explicit` search operator (Spotify does), so the Apple path
fetches candidates and filters on `contentRating` client-side.

These four outcomes surface in the UI as `MatchMethodChip` values: ISRC match,
ISRC → clean, Search match, Search → clean. See
`screenshots/job-completed__light__desktop.png`.

> **Design:** users care about *"did this track get cleaned, and how confident
> should I be?"* The current four-way method chip exposes implementation detail.
> Consider whether provenance should be progressive disclosure rather than a
> column.

---

## Auto-Sync build dependency

Sync applies a delta — **additions and removals**. Removal does not exist on the
Apple path today:

- `IMusicService`, the shared abstraction, is **add-only**: it has
  `AddTracksToPlaylistAsync` and no remove method at all.
- `RemoveTracksFromPlaylistAsync` exists only on `ISpotifyService`.
- `PlaylistSyncService` depends on `ISpotifyService` directly.

Apple-capable sync therefore requires extending the shared interface and
implementing removal against Apple's library API — not just swapping a
dependency. This is an engineering prerequisite for the redesign shipping, and
the finished sync feature should be designed as fully working.

---

## Plan limits that surface in the UI

| Limit | Value | Behavior |
|---|---|---|
| Sync configs per user | 10 | HTTP 403 with a plan-limit error |
| Tracks per playlist | 200 | **Advertised in plan features but not enforced anywhere in code** |
| Sync requires subscription | — | HTTP 400 "Active subscription required to enable sync" |
| Sync cadence | daily 00:01 UTC / weekly / manual | |
| Job retry | 2 attempts (30s, 120s backoff) | |
| Token refresh failures | 5 consecutive disables auto-refresh | Spotify-only; Apple has no refresh |

> **Note:** the 200-tracks-per-playlist limit is shown to users as a plan feature
> but is not enforced server-side. Either enforce it or stop advertising it —
> a designed limit that does not exist is worse than no limit.
