/**
 * Design screenshot capture harness.
 *
 * Drives a real Next dev server (pointed at tools/design-capture/mock-server.mjs)
 * and writes one PNG per screen x state x theme x viewport into
 * docs/design/screenshots/.
 *
 * Usage:
 *   node tools/design-capture/capture.mjs                  # everything
 *   node tools/design-capture/capture.mjs --only=dashboard # filter by shot id
 *   node tools/design-capture/capture.mjs --themes=dark
 *   node tools/design-capture/capture.mjs --viewports=mobile
 *
 * Assumes the web app and mock server are already running; see the README in
 * this directory, or use `pnpm design:capture` which starts both.
 */

import { chromium } from '@playwright/test';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { NOW } from './fixtures/scenarios.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '../..');
const OUT_DIR = path.join(REPO_ROOT, 'docs/design/screenshots');

const BASE_URL = process.env.RW_BASE_URL || 'http://127.0.0.1:3000';
const MOCK_URL = `http://127.0.0.1:${process.env.RW_MOCK_PORT || 5199}`;

const VIEWPORTS = {
  desktop: { width: 1440, height: 900 },
  mobile: { width: 390, height: 844 }, // iPhone 14
};

const THEMES = ['light', 'dark'];

/**
 * Each shot names the scenario the mock server should serve, the route to
 * visit, and optionally a `prepare` hook for interaction-dependent states
 * (open menus, expanded panels).
 *
 * `waitFor` is a selector that must be visible before we shoot — never a
 * fixed sleep, which would be flaky under varying dev-server compile times.
 */
const SHOTS = [
  // ---- public / unauthenticated -----------------------------------------
  {
    id: 'landing',
    route: '/',
    scenario: 'dashboard-empty',
    anonymous: true,
    fullPage: true,
    waitFor: 'text=/RadioWash/i',
  },
  {
    id: 'auth',
    route: '/auth',
    scenario: 'dashboard-empty',
    anonymous: true,
    waitFor: 'button',
  },
  {
    id: 'auth-error',
    route: '/auth?error=Could+not+authenticate+with+Spotify.+Please+try+again.',
    scenario: 'dashboard-empty',
    anonymous: true,
    waitFor: '[role="alert"], button',
  },

  // ---- dashboard ---------------------------------------------------------
  {
    id: 'dashboard-empty',
    route: '/dashboard',
    scenario: 'dashboard-empty',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'dashboard-spotify-only',
    route: '/dashboard',
    scenario: 'dashboard-spotify-only',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'dashboard-both-connected',
    route: '/dashboard',
    scenario: 'dashboard-both-connected',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'dashboard-apple-reconnect',
    route: '/dashboard',
    scenario: 'dashboard-apple-reconnect',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'dashboard-apple-only',
    route: '/dashboard',
    scenario: 'dashboard-apple-only',
    fullPage: true,
    waitFor: 'header',
  },
  {
    // The avatar dropdown is the app's only nav surface — worth its own shot.
    id: 'dashboard-user-menu',
    route: '/dashboard',
    scenario: 'dashboard-both-connected',
    waitFor: 'header',
    prepare: async (page) => {
      // Radix DropdownMenuTrigger renders aria-haspopup="menu"; matching that is
      // sturdier than positional guessing among the header's buttons.
      const trigger = page.locator('header [aria-haspopup="menu"]').first();
      await trigger.waitFor({ state: 'visible', timeout: 15000 });

      // The trigger is in the SSR markup but inert until React hydrates, so an
      // early click is silently swallowed. Poll the click until the menu opens
      // instead of guessing a hydration delay with a fixed sleep.
      const menu = page.locator('[role="menu"]');
      for (let attempt = 0; attempt < 12; attempt++) {
        if (await menu.isVisible().catch(() => false)) return;
        await trigger.click().catch(() => {});
        await page.waitForTimeout(500);
      }
      if (!(await menu.isVisible().catch(() => false))) {
        throw new Error('user menu never opened after hydration retries');
      }
    },
  },

  // ---- job detail --------------------------------------------------------
  {
    id: 'job-completed',
    route: '/jobs/101',
    scenario: 'job-completed',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'job-processing',
    route: '/jobs/102',
    scenario: 'job-processing',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'job-failed',
    route: '/jobs/103',
    scenario: 'job-failed',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'job-pending',
    route: '/jobs/104',
    scenario: 'job-pending',
    fullPage: true,
    waitFor: 'header',
  },

  // ---- sync --------------------------------------------------------------
  {
    id: 'sync-free',
    route: '/dashboard/sync',
    scenario: 'sync-free',
    fullPage: true,
    waitFor: 'header',
  },
  {
    id: 'sync-pro',
    route: '/dashboard/sync',
    scenario: 'sync-pro',
    fullPage: true,
    waitFor: 'header',
  },

  // ---- subscription ------------------------------------------------------
  {
    id: 'subscription-free',
    route: '/subscription',
    scenario: 'subscription-free',
    fullPage: true,
    waitFor: 'header, main',
  },
  {
    id: 'subscription-active',
    route: '/subscription',
    scenario: 'subscription-active',
    fullPage: true,
    waitFor: 'header, main',
  },
  {
    id: 'subscription-canceling',
    route: '/subscription',
    scenario: 'subscription-canceling',
    fullPage: true,
    waitFor: 'header, main',
  },
  {
    id: 'subscription-cancel',
    route: '/subscription/cancel',
    scenario: 'subscription-free',
    waitFor: 'main, h1',
  },
];

const args = process.argv.slice(2);
const argVal = (name) => {
  const hit = args.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.split('=')[1] : undefined;
};

const only = argVal('only');
const themes = (argVal('themes') || THEMES.join(',')).split(',');
const viewports = (argVal('viewports') || Object.keys(VIEWPORTS).join(',')).split(',');

const setScenario = async (scenario) => {
  const res = await fetch(`${MOCK_URL}/__scenario`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ scenario }),
  });
  if (!res.ok) {
    throw new Error(`mock server rejected scenario "${scenario}": ${res.status}`);
  }
};

const preflight = async () => {
  const problems = [];
  try {
    const r = await fetch(`${MOCK_URL}/__health`, { signal: AbortSignal.timeout(3000) });
    if (!r.ok) problems.push(`mock server unhealthy at ${MOCK_URL}`);
  } catch {
    problems.push(`mock server not reachable at ${MOCK_URL} — start it with: node tools/design-capture/mock-server.mjs`);
  }
  try {
    const r = await fetch(BASE_URL, { signal: AbortSignal.timeout(10000) });
    if (!r.ok && r.status >= 500) problems.push(`web app returned ${r.status} at ${BASE_URL}`);
  } catch {
    problems.push(`web app not reachable at ${BASE_URL} — start it with: pnpm design:web`);
  }
  if (problems.length) {
    console.error('\nPreflight failed:\n' + problems.map((p) => `  - ${p}`).join('\n') + '\n');
    process.exit(1);
  }
};

/**
 * A signed-in Supabase session lives in a cookie named sb-<ref>-auth-token.
 * @supabase/ssr parses it, then validates by calling GET /auth/v1/user — which
 * our mock server answers. So the cookie contents only need to be structurally
 * valid, not cryptographically real.
 */
const authCookie = () => {
  // @supabase/ssr names the cookie sb-<ref>-auth-token, where <ref> is the first
  // hostname label of NEXT_PUBLIC_SUPABASE_URL. Under capture that URL points at
  // the mock server (127.0.0.1), so the ref is "127". This must stay in sync with
  // the NEXT_PUBLIC_SUPABASE_URL set by the design:web script.
  const supabaseUrl =
    process.env.NEXT_PUBLIC_SUPABASE_URL || `http://127.0.0.1:${process.env.RW_MOCK_PORT || 5199}`;
  const ref = (() => {
    try {
      return new URL(supabaseUrl).hostname.split('.')[0];
    } catch {
      return '127';
    }
  })();
  const session = {
    access_token: 'design-capture-fake-token',
    token_type: 'bearer',
    expires_in: 3600,
    expires_at: 4102444800,
    refresh_token: 'design-capture-fake-refresh',
    user: {
      id: '00000000-0000-4000-8000-000000000001',
      aud: 'authenticated',
      role: 'authenticated',
      email: 'alex@example.com',
      app_metadata: { provider: 'spotify', providers: ['spotify'] },
      user_metadata: { name: 'Alex Rivera' },
      created_at: '2026-07-02T16:20:00.000Z',
    },
  };
  const url = new URL(BASE_URL);
  return {
    name: `sb-${ref}-auth-token`,
    value: `base64-${Buffer.from(JSON.stringify(session)).toString('base64')}`,
    domain: url.hostname,
    path: '/',
    httpOnly: false,
    secure: false,
    sameSite: 'Lax',
  };
};

const run = async () => {
  await preflight();

  if (existsSync(OUT_DIR)) await rm(OUT_DIR, { recursive: true, force: true });
  await mkdir(OUT_DIR, { recursive: true });

  const browser = await chromium.launch();
  const shots = SHOTS.filter((s) => !only || s.id.includes(only));
  const manifest = [];
  const failures = [];

  for (const viewportName of viewports) {
    const viewport = VIEWPORTS[viewportName];
    if (!viewport) {
      console.warn(`[capture] unknown viewport "${viewportName}", skipping`);
      continue;
    }

    for (const theme of themes) {
      const context = await browser.newContext({
        viewport,
        colorScheme: theme,
        deviceScaleFactor: 2, // retina — Design reads type and spacing better
        timezoneId: 'UTC',
        locale: 'en-US',
      });

      // next-themes writes the class from localStorage; set it so the very
      // first paint is already in the right theme (no flash mid-screenshot).
      await context.addInitScript((t) => {
        try {
          window.localStorage.setItem('theme', t);
        } catch {
          /* storage unavailable — the cookie/colorScheme fallback still applies */
        }
      }, theme);

      /**
       * Stub Apple's MusicKit global before any app code runs.
       *
       * useMusicKit injects https://js-cdn.music.apple.com/... and configures it
       * with a developer token. Against the mock that token is fake, so MusicKit
       * reports "Apple Music is unavailable: Invalid token" — an error banner
       * that contradicts the Connected state we're trying to photograph.
       * Presenting a ready instance keeps the card in its real connected look.
       */
      await context.addInitScript(() => {
        const instance = {
          authorize: async () => 'design-capture-fake-music-user-token',
          unauthorize: async () => undefined,
          isAuthorized: true,
        };
        window.MusicKit = {
          configure: async () => instance,
          getInstance: () => instance,
        };
        // useMusicKit waits on this event when it finds an existing script tag.
        setTimeout(() => document.dispatchEvent(new Event('musickitloaded')), 0);
      });

      for (const shot of shots) {
        if (!shot.anonymous) await context.addCookies([authCookie()]);

        const page = await context.newPage();
        await page.setExtraHTTPHeaders({ 'x-rw-scenario': shot.scenario });

        /**
         * Stall SignalR hub traffic instead of failing it.
         *
         * JobCard prefers live progress only when progressState.status is not
         * 'idle' (JobCard.tsx:128); otherwise it renders job.processedTracks /
         * job.totalTracks straight from our fixture. A *failed* negotiate drives
         * the hook to 'failed', which zeroes the numbers AND prints
         * "Connection error: ...". Never answering leaves it at 'connecting'
         * with connectionError unset, so the fixture's real 88-of-187 progress
         * is what lands in the screenshot.
         *
         * Note the glob has no trailing slash: the hub path ends at
         * /hubs/playlist-progress, so '**\/hubs/**' would not match it.
         */
        await page.route('**/hubs/playlist-progress**', () => {
          /* deliberately never resolved */
        });
        // The hook also fires an unawaited /api/healthcheck probe; 404 noise
        // from the mock is harmless but this keeps the console clean.
        await page.route('**/api/healthcheck**', (route) =>
          route.fulfill({ status: 200, body: '{}' })
        );

        // Apple's MusicKit CDN is unreachable-by-design here; the init script
        // above already provides a stubbed global.
        await page.route('**js-cdn.music.apple.com/**', (route) => route.abort());

        const name = `${shot.id}__${theme}__${viewportName}.png`;
        const dest = path.join(OUT_DIR, name);

        try {
          await setScenario(shot.scenario);
          // 'networkidle' never settles against the dev server: HMR holds a
          // websocket open for the life of the page. Wait for DOM instead, then
          // gate on the shot's own selector.
          await page.goto(`${BASE_URL}${shot.route}`, {
            waitUntil: 'domcontentloaded',
            timeout: 45000,
          });

          if (shot.waitFor) {
            await page.waitForSelector(shot.waitFor, { timeout: 20000 });
          }
          if (shot.prepare) await shot.prepare(page);

          // Freeze anything still animating (spinners, progress bars) so two
          // runs of the same shot produce identical pixels.
          await page.addStyleTag({
            content: `
            *, *::before, *::after {
              animation-play-state: paused !important;
              transition: none !important;
              caret-color: transparent !important;
            }
            /* Next.js dev-only chrome: the issues badge and devtools indicator.
               Not product UI — it must never reach a design reference shot. */
            nextjs-portal,
            [data-nextjs-toast],
            [data-nextjs-dialog-overlay],
            #__next-build-watcher,
            [data-nextjs-devtools-indicator] { display: none !important; }
          `,
          });
          // Client components hydrate and fetch after DOMContentLoaded (provider
          // status cards, React Query). Wait for the font/layout to settle rather
          // than for network silence, which HMR prevents.
          await page.evaluate(() => document.fonts?.ready);
          await page.waitForTimeout(600);

          await page.screenshot({ path: dest, fullPage: !!shot.fullPage });
          manifest.push({
            file: name,
            id: shot.id,
            route: shot.route,
            scenario: shot.scenario,
            theme,
            viewport: viewportName,
          });
          console.log(`  ✓ ${name}`);
        } catch (error) {
          const msg = error instanceof Error ? error.message : String(error);
          failures.push({ shot: shot.id, theme, viewport: viewportName, error: msg });
          console.error(`  ✗ ${name} — ${msg.split('\n')[0]}`);
        } finally {
          await page.close();
        }
      }

      await context.close();
    }
  }

  await browser.close();

  await writeFile(
    path.join(OUT_DIR, 'manifest.json'),
    JSON.stringify(
      { capturedAtFixtureTime: NOW, baseUrl: BASE_URL, shots: manifest, failures },
      null,
      2
    ) + '\n'
  );

  console.log(`\n${manifest.length} screenshots → docs/design/screenshots/`);
  if (failures.length) {
    console.error(`${failures.length} failed — see manifest.json`);
    process.exit(1);
  }
};

run().catch((error) => {
  console.error(error);
  process.exit(1);
});
