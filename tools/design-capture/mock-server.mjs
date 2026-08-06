/**
 * Mock backend for design screenshot capture.
 *
 * Impersonates two upstreams at once:
 *   1. The RadioWash .NET API  (/api/*)
 *   2. Supabase GoTrue auth    (/auth/v1/*)
 *
 * Why both, and why a real HTTP server instead of Playwright's page.route:
 * the dashboard, job, and sync pages are React Server Components. They call
 * fetchWithSupabaseAuthServer() and supabase.auth.getUser() from Node, inside
 * the Next process. page.route only sees browser traffic, so it cannot mock
 * them. Pointing NEXT_PUBLIC_API_URL / NEXT_PUBLIC_SUPABASE_URL at this server
 * intercepts both server-side and client-side calls with one mechanism.
 *
 * Scenario selection, in priority order:
 *   1. ?scenario= query param
 *   2. x-rw-scenario request header (set by the browser via page.setExtraHTTPHeaders)
 *   3. the server's current default, set by POST /__scenario (used for RSC loads,
 *      which carry neither of the above)
 */

import { createServer } from 'node:http';
import { scenarios, DEFAULT_SCENARIO } from './fixtures/scenarios.mjs';

const PORT = Number(process.env.RW_MOCK_PORT || 5199);

// RSC requests reach us with no scenario hint, so the capture script sets this
// over POST /__scenario before navigating.
let currentScenario = process.env.RW_SCENARIO || DEFAULT_SCENARIO;

const json = (res, body, status = 200) => {
  const payload = JSON.stringify(body ?? null);
  res.writeHead(status, {
    'content-type': 'application/json',
    'access-control-allow-origin': '*',
    'access-control-allow-headers': '*',
    'access-control-allow-methods': 'GET,POST,PATCH,DELETE,OPTIONS',
    'cache-control': 'no-store',
  });
  res.end(payload);
};

const pickScenario = (url, req) => {
  const q = url.searchParams.get('scenario');
  if (q && scenarios[q]) return scenarios[q];
  const h = req.headers['x-rw-scenario'];
  if (typeof h === 'string' && scenarios[h]) return scenarios[h];
  return scenarios[currentScenario] ?? scenarios[DEFAULT_SCENARIO];
};

/**
 * A Supabase user object shaped like what @supabase/ssr expects back from
 * GET /auth/v1/user. Only the fields the app actually reads need to be real.
 */
const supabaseUser = (s) => ({
  id: '00000000-0000-4000-8000-000000000001',
  aud: 'authenticated',
  role: 'authenticated',
  email: s.user.email,
  email_confirmed_at: '2026-07-02T16:20:00.000Z',
  phone: '',
  confirmed_at: '2026-07-02T16:20:00.000Z',
  last_sign_in_at: '2026-08-04T06:00:00.000Z',
  app_metadata: { provider: 'spotify', providers: ['spotify'] },
  user_metadata: {
    name: s.user.displayName,
    full_name: s.user.displayName,
    email: s.user.email,
    avatar_url: s.user.profileImageUrl,
  },
  identities: [],
  created_at: '2026-07-02T16:20:00.000Z',
  updated_at: '2026-08-04T06:00:00.000Z',
  is_anonymous: false,
});

const server = createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  const path = url.pathname;
  const method = req.method || 'GET';

  if (method === 'OPTIONS') return json(res, null, 204);

  // ---- control plane -----------------------------------------------------
  if (path === '/__scenario' && method === 'POST') {
    const body = await new Promise((resolve) => {
      let raw = '';
      req.on('data', (c) => (raw += c));
      req.on('end', () => resolve(raw));
    });
    try {
      const { scenario } = JSON.parse(body || '{}');
      if (!scenarios[scenario]) {
        return json(res, { error: `unknown scenario: ${scenario}` }, 400);
      }
      currentScenario = scenario;
      return json(res, { ok: true, scenario });
    } catch {
      return json(res, { error: 'bad json' }, 400);
    }
  }
  if (path === '/__health') return json(res, { ok: true, scenario: currentScenario });

  const s = pickScenario(url, req);

  // ---- supabase auth -----------------------------------------------------
  // getUser() and getSession() both resolve through here.
  if (path.startsWith('/auth/v1/user')) {
    return json(res, supabaseUser(s));
  }
  if (path.startsWith('/auth/v1/token')) {
    return json(res, {
      access_token: 'design-capture-fake-token',
      token_type: 'bearer',
      expires_in: 3600,
      expires_at: 4102444800,
      refresh_token: 'design-capture-fake-refresh',
      user: supabaseUser(s),
    });
  }
  if (path.startsWith('/auth/v1/logout')) return json(res, {}, 204);

  // ---- radiowash api -----------------------------------------------------
  if (path === '/api/auth/me') return json(res, s.user);

  // GET /api/auth/status/{provider}
  const statusMatch = path.match(/^\/api\/auth\/status\/([^/]+)$/);
  if (statusMatch) {
    const provider = statusMatch[1];
    return json(res, s.connections?.[provider] ?? { connected: false, canRefresh: false });
  }

  if (path === '/api/auth/musickit/devtoken') {
    return json(res, { token: 'design-capture-fake-musickit-token' });
  }

  if (path === '/api/playlist/user/me') return json(res, s.playlists ?? []);

  const tracksMatch = path.match(/^\/api\/playlist\/playlist\/([^/]+)\/tracks$/);
  if (tracksMatch) return json(res, []);

  if (path === '/api/cleanplaylist/user/me/jobs') return json(res, s.jobs ?? []);

  // GET /api/cleanplaylist/user/me/job/{id}  and  /user/{userId}/job/{id}
  const jobMatch = path.match(/^\/api\/cleanplaylist\/user\/[^/]+\/job\/(\d+)$/);
  if (jobMatch) {
    const id = Number(jobMatch[1]);
    const job = s.job ?? (s.jobs ?? []).find((j) => j.id === id);
    if (!job) return json(res, { title: 'Job not found' }, 404);
    return json(res, job);
  }

  const jobTracksMatch = path.match(
    /^\/api\/cleanplaylist\/user\/[^/]+\/job\/(\d+)\/tracks$/
  );
  if (jobTracksMatch) return json(res, s.trackMappings ?? []);

  if (path === '/api/subscription/status') return json(res, s.subscription);
  if (path === '/api/subscription/current') {
    if (!s.subscription?.hasActiveSubscription) return json(res, null);
    return json(res, {
      id: s.subscription.subscriptionId,
      status: s.subscription.status,
      currentPeriodStart: '2026-08-04T12:00:00.000Z',
      currentPeriodEnd: s.subscription.currentPeriodEnd,
      cancelAtPeriodEnd: s.subscription.cancelAtPeriodEnd,
      plan: (s.plans ?? [])[0] ?? null,
      createdAt: '2026-07-04T12:00:00.000Z',
    });
  }
  if (path === '/api/subscription/plans') {
    return json(res, s.plans ?? []);
  }

  if (path === '/api/playlistsync' && method === 'GET') {
    return json(res, s.syncConfigs ?? []);
  }
  const syncHistoryMatch = path.match(/^\/api\/playlistsync\/(\d+)\/history$/);
  if (syncHistoryMatch) return json(res, s.syncHistory ?? []);

  // Unhandled routes must be loud. A silent 200 would render a screenshot that
  // looks fine but is missing data, which is worse than a visible failure.
  console.warn(`[mock] unhandled ${method} ${path}`);
  return json(res, { title: 'Not mocked', path, method }, 404);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`[mock] listening on http://127.0.0.1:${PORT} (scenario: ${currentScenario})`);
});
