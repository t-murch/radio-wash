import { act, render, screen } from '@testing-library/react';
import { beforeEach, describe, it, expect, vi } from 'vitest';

import LandingPage from '../LandingPage';
import { DEFINITION, FAQ } from '@/lib/content/landing';
import { MARKETING_ROUTES } from '@/lib/routes';

vi.mock('../../../auth/actions', () => ({ signOut: vi.fn() }));

// The HeroCta island reads the session through the browser Supabase client.
// The vitest env stubs point that client at localhost, so a real getSession()
// would try the network; the mock keeps the island deterministic instead.
const { mockGetSession } = vi.hoisted(() => ({ mockGetSession: vi.fn() }));

vi.mock('@/lib/supabase/client', () => ({
  createClient: () => ({
    auth: {
      getSession: mockGetSession,
      onAuthStateChange: vi.fn(() => ({
        data: { subscription: { unsubscribe: vi.fn() } },
      })),
    },
  }),
}));

beforeEach(() => {
  mockGetSession.mockResolvedValue({ data: { session: null } });
});

// Flushes the island's initial getSession() promise inside act so the session
// state settles before assertions run.
async function renderLanding() {
  const view = render(<LandingPage />);
  await act(() => Promise.resolve());
  return view;
}

describe('LandingPage', () => {
  it('names Apple Music and never mentions Spotify', async () => {
    const { container } = await renderLanding();

    expect(screen.getByText(/for apple music/i)).toBeInTheDocument();
    // Whole-tree text match so a split across inline elements can't slip by.
    expect(container.textContent ?? '').not.toMatch(/spotify/i);
  });

  it('says what RadioWash is in one server-rendered sentence', async () => {
    await renderLanding();

    expect(screen.getByText(DEFINITION)).toBeInTheDocument();
  });

  it('leads with the specimen instead of fabricated social proof', async () => {
    await renderLanding();

    // No users yet, so testimonials or counts would have to be invented.
    expect(screen.queryByText(/testimonial|trusted by|users love/i)).toBeNull();

    expect(screen.getByText('Without Me')).toBeInTheDocument();
    expect(screen.getByText('Mr. Brightside')).toBeInTheDocument();
    // "rockstar" appears twice by design — once as a row, once in the caption
    // that explains why it is missing from the copy.
    expect(screen.getAllByText('rockstar')).toHaveLength(2);
  });

  it('teaches above the fold that a clean copy can be shorter', async () => {
    await renderLanding();

    // The single most misunderstood thing about the product.
    expect(screen.getByText('Not in the copy')).toBeInTheDocument();
    expect(screen.getByText(/no clean version exists/i)).toBeInTheDocument();
    expect(
      screen.getByText(/sometimes shorter than its source/i)
    ).toBeInTheDocument();
  });

  it('states the subscription requirement rather than burying it', async () => {
    await renderLanding();

    expect(
      screen.getByText(/needs an active apple music subscription/i)
    ).toBeInTheDocument();
  });

  it('renders every FAQ entry from the shared source', async () => {
    await renderLanding();

    for (const item of FAQ) {
      expect(screen.getByText(item.question)).toBeInTheDocument();
    }
  });

  it('greets a signed-in visitor with the dashboard, not a signup pitch', async () => {
    mockGetSession.mockResolvedValue({
      data: { session: { user: { email: 'someone@example.com' } } },
    });

    await renderLanding();

    expect(
      await screen.findByRole('link', { name: /go to your dashboard/i })
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('link', { name: /make a clean copy/i })
    ).not.toBeInTheDocument();
    // The subscription line is guidance for signing up; it drops once signed in.
    expect(
      screen.queryByText(/needs an active apple music subscription/i)
    ).not.toBeInTheDocument();
    expect(screen.getByText(/someone@example.com/)).toBeInTheDocument();
  });

  it('links to the legal pages the footer promises', async () => {
    await renderLanding();

    expect(screen.getByRole('link', { name: /privacy/i })).toHaveAttribute(
      'href',
      '/privacy'
    );
    expect(screen.getByRole('link', { name: /terms/i })).toHaveAttribute(
      'href',
      '/terms'
    );
  });

  it('links to the content pages from the nav, footer, and FAQ', async () => {
    await renderLanding();

    // Nav, footer, and the FAQ follow-up all point at how-it-works.
    const howItWorksLinks = screen.getAllByRole('link', {
      name: /how it works|how matching works/i,
    });
    expect(howItWorksLinks.length).toBeGreaterThanOrEqual(2);
    for (const link of howItWorksLinks) {
      expect(link).toHaveAttribute('href', MARKETING_ROUTES.howItWorks);
    }

    expect(
      screen.getByRole('link', { name: /clean-playlist guide/i })
    ).toHaveAttribute('href', MARKETING_ROUTES.cleanPlaylistGuide);
  });
});
