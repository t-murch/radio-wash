# Design capture harness

Generates the screenshot set in `docs/design/screenshots/` used as seed data for
design work (e.g. handing current-state UI to Claude Design).

76 shots: 19 screens/states × 2 themes × 2 viewports.

## Running it

Three terminals, or three background processes:

```bash
# 1. Mock backend (impersonates both the .NET API and Supabase auth)
pnpm design:mock

# 2. Next dev server, pointed at the mock
pnpm design:web

# 3. Capture
pnpm design:capture
```

Output lands in `docs/design/screenshots/`, one PNG per shot plus a
`manifest.json` recording what was captured and anything that failed.

The capture script preflights both servers and exits with a usage hint if
either is unreachable, so order only matters in that both must be up before
step 3.

### Filters

```bash
node tools/design-capture/capture.mjs --only=dashboard   # substring match on shot id
node tools/design-capture/capture.mjs --themes=dark
node tools/design-capture/capture.mjs --viewports=mobile
```

Note that any run rewrites `manifest.json` for just the shots it captured. Do a
full run when you want a complete manifest.

## How it works

Most screens are behind Supabase auth and a live provider connection, and the
interesting states (a failed job, a job stuck at 47%, an Apple token about to
expire) can't be produced on demand against a real backend. So the harness
fakes the backend rather than the frontend.

**`mock-server.mjs`** answers on one port for two upstreams:

- `/api/*` — the RadioWash .NET API
- `/auth/v1/*` — Supabase GoTrue

`pnpm design:web` points `NEXT_PUBLIC_API_URL`, `API_BASE_URL`, and
`NEXT_PUBLIC_SUPABASE_URL` at it.

This has to be a real HTTP server rather than Playwright's `page.route`,
because the dashboard, job, and sync pages are React Server Components: they
call `fetchWithSupabaseAuthServer()` and `supabase.auth.getUser()` from inside
the Next process. `page.route` only sees browser traffic and would miss them
entirely.

**Auth** is a `sb-<ref>-auth-token` cookie holding a structurally valid but
fake session. `@supabase/ssr` parses it and then validates by calling
`GET /auth/v1/user` — which the mock answers — so the cookie never needs to be
cryptographically real.

**Scenarios** live in `fixtures/scenarios.mjs`, one per screenshot. The capture
script selects one via `POST /__scenario` before navigating (RSC loads carry no
usable per-request hint), and also sends an `x-rw-scenario` header for
browser-side calls.

## Fixture conventions

- **Shapes mirror the real contracts** — `web/src/app/services/api.ts` and
  `api/Models/DTO/`. If an API shape changes, these must follow.
- **All timestamps are fixed literals.** Relative dates would make every run
  produce different pixels and defeat visual diffing.
- **Data is deliberately awkward**: a 60-character playlist name, a 1-track
  playlist, an empty description. Tidy 3-word names hide the layout bugs a
  redesign needs to find.
- **Apple library playlists report `trackCount: 0`**, because
  `AppleMusicMusicService` omits the attribute. That's a real constraint, so
  the fixture reproduces it rather than papering over it.

## Browser-side stubs

Three things are neutralized in `capture.mjs` so screenshots show product UI
rather than local-environment artifacts:

| What | Why |
|---|---|
| `window.MusicKit` stubbed | Apple's CDN rejects the mock's fake developer token, producing an "Apple Music is unavailable: Invalid token" banner that contradicts the Connected state being photographed. |
| SignalR negotiate stalled (never answered) | `JobCard` prefers live progress when `progressState.status !== 'idle'`. A *failed* negotiate moves the hook to `failed`, which zeroes the counters and prints "Connection error: …". Never answering leaves it `connecting`, so the fixture's real progress (88 of 187) is what renders. |
| Next.js dev overlay hidden via CSS | The issues badge and devtools indicator are dev chrome, not product UI. |

Animations are frozen and `document.fonts.ready` is awaited before each shot so
repeat runs are pixel-stable.

## Known limitations

- Runs against the **dev** server, so fonts and bundle-dependent rendering may
  differ slightly from a production build.
- `waitUntil: 'networkidle'` is unusable here — HMR holds a websocket open for
  the page's lifetime. Shots gate on `domcontentloaded` plus a per-shot
  selector instead.
- The `dashboard-user-menu` shot polls its click until the Radix menu opens;
  the trigger is present in SSR markup but inert until React hydrates.
- Playlist artwork renders as "No Image" placeholders — fixtures set no
  `imageUrl`, since real CDN art would be non-deterministic and off-limits to
  redistribute.
