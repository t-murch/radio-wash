# Screen inventory

Every screen in the current app, its states, and the screenshots covering them.

Every shot exists in 4 variants. Filenames follow
`<id>__<theme>__<viewport>.png`, where theme is `light`|`dark` and viewport is
`desktop` (1440×900) or `mobile` (390×844), all at 2× scale.

**These show the current Spotify-centric app — the "before."**

`→` marks what the Apple-only redesign changes.

---

## Public

### Landing — `/`
`screenshots/landing__*.png`

Marketing page with its own nav and footer, separate from the app's
`GlobalHeader`. Sections: hero, problem (3 emoji cards), a fake before/after
playlist mock, 6-item feature grid, 2 testimonials with placeholder avatars,
4-item FAQ, gradient CTA, footer.

→ **Rewrite entirely.** Apple Music is mentioned zero times on this page today.
All copy, the page title, SEO metadata, the OG image, and the JSON-LD are
Spotify-only.

### Sign in — `/auth`
`screenshots/auth__*.png`, `screenshots/auth-error__*.png`

Two OAuth buttons ("Sign up with Spotify" / "Sign up with Apple") and an error
alert driven by `?error=`.

Note: both buttons currently render green — the Spotify button uses `bg-success`
and the Apple button uses `bg-primary`, which is *also* Spotify green in this
theme.

→ **Spotify removed; Google and email added** (brief §13). Three methods —
Apple, Google, email — none of which grant music access. This screen is step one
of two; see brief §6. Email sign-in also needs enter-email, check-inbox, and
expired-link screens that do not exist today.

---

## Dashboard — `/dashboard`

The app's main surface, and the screen most changed by the redesign.

Current structure, top to bottom: two provider connection cards side by side →
provider tab bar → "Clean or Copy a Playlist" panel (playlist select, a
clean-vs-copy radio pair, optional name, submit) → playlist grid. A "Job Status"
column runs down the right.

| State | Screenshot | What it shows |
|---|---|---|
| Empty | `dashboard-empty__*` | New user: nothing connected, no playlists, no jobs |
| Spotify only | `dashboard-spotify-only__*` | Today's common case |
| Both connected | `dashboard-both-connected__*` | Full dual-provider UI |
| Apple reconnect | `dashboard-apple-reconnect__*` | Token inside the 14-day window; Reconnect button appears |
| **Apple only** | `dashboard-apple-only__*` | **See below** |
| User menu open | `dashboard-user-menu__*` | The app's only nav: Dashboard, Sync, subscription w/ Pro-Free badge, feedback, sign out |

### The Apple-only screenshot is the key artifact

`screenshots/dashboard-apple-only__light__desktop.png` shows a user with **only**
Apple Music connected. The dashboard defaults to the **Spotify tab** and displays
*"Connect Spotify to Get Started"* — while Apple Music is connected, has
playlists, and sits unselected one tab over.

This is the Spotify-centric structural assumption stated as plainly as it can be.

→ **Rebuild.** Two connection cards collapse to one connection state. The tab
bar disappears. The clean-vs-copy radio pair becomes a single clean action —
"copy to another service" has no meaning with one service. Roughly the top third
of the current screen is spent on choices that will no longer exist.

Note also: playlist cards are written **twice** in `dashboard-client.tsx` — a
desktop grid variant and a separate mobile list variant with duplicated markup.

---

## Job detail — `/jobs/[jobId]`

Header (source → target, job type, status badge), progress block, then the
`TrackMappings` table.

| State | Screenshot | Notes |
|---|---|---|
| Pending | `job-pending__*` | Queued, no progress yet |
| Processing | `job-processing__*` | Live SignalR progress — "88 of 187 tracks processed" |
| Completed | `job-completed__*` | Full track mapping table, all four match-method chips |
| Failed | `job-failed__*` | Error message surfaced |

The mappings table shows original → matched version with a `MatchMethodChip`:
ISRC match, ISRC → clean, Search match, Search → clean. Filter tabs above it:
All / Explicit / Clean / Unmatched.

→ Largely survives. Two things to reconsider: per-track links vanish on Apple
(no public URL for library items), and the four-way method chip exposes
implementation detail users may not need as a column.

---

## Auto-Sync — `/dashboard/sync`

| State | Screenshot | Notes |
|---|---|---|
| Free | `sync-free__*` | Paid feature, gated |
| Pro | `sync-pro__*` | One healthy daily config, one auto-disabled after failure |

Per config: source → target, active toggle, frequency (daily/weekly/manual),
last sync status, next scheduled run, manual trigger, history.

→ Stays, and becomes Apple-capable. This currently **only works on Spotify** —
`PlaylistSyncService` depends on `ISpotifyService` directly. See
`constraints.md` for the removal-path dependency.

---

## Subscription — `/subscription`

| State | Screenshot | Notes |
|---|---|---|
| Free | `subscription-free__*` | Pricing for the $5/mo Sync Plan |
| Active | `subscription-active__*` | Manage / portal access |
| Canceling | `subscription-canceling__*` | `cancelAtPeriodEnd` — distinct copy from plain active |
| Cancel return | `subscription-cancel__*` | Stripe checkout abandoned |

Plan: **Sync Plan, $5.00/mo** — daily auto sync, up to 10 sync configs, up to 200
tracks/playlist, manual triggering, history, smart matching. Free tier is
implicit: no subscription row means one-off cleaning only.

→ Stays. Note the 200-track limit is advertised but not enforced in code.

**Not captured:** `/subscription/success` requires a live Stripe `session_id`
and reconciles against the API, so it cannot be faked meaningfully. Design it
from the cancel-return screen plus the flow description.

---

## Chrome

**`GlobalHeader`** — sticky bar, "RadioWash" wordmark (hardcoded
`text-green-600`, bypassing the token system), theme toggle, avatar dropdown.
The app's only nav. Not used by the landing page, which has its own.

**`FloatingFeedbackButton`** — fixed bottom-right pill, Sentry-backed. Visible in
most app screenshots.

**`ServiceUnavailableBanner`** — shown when `NEXT_PUBLIC_SERVICE_AVAILABLE` is
not `'true'`. Copy explicitly blames "Spotify API limitations for development
applications" and disables the landing CTAs. Not captured (disabled during
capture). → Rewrite or remove.

---

## Routes that do not exist

Gaps the redesign should address:

- **No onboarding / welcome route.** New users land on a full dashboard.
- **No settings or account page.** Only the dropdown.
- **No legal pages** — no privacy, no terms. `sitemap.ts` lists only `/`.
- **No modal primitive anywhere** — `components/ui/` has no `dialog.tsx`.
- **Share is dead code** — `ShareSuccessModal`, `SharePlaylistButton`, and
  `ShareCard` total ~800 lines with their JSX commented out. Revive or delete.
