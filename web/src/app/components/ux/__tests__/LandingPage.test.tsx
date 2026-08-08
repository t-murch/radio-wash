import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';

import LandingPage from '../LandingPage';
import { FAQ } from '@/lib/content/landing';

vi.mock('../../../auth/actions', () => ({ signOut: vi.fn() }));

describe('LandingPage', () => {
  it('names Apple Music and never mentions Spotify', () => {
    render(<LandingPage />);

    expect(screen.getByText(/for apple music/i)).toBeInTheDocument();
    expect(screen.queryByText(/spotify/i)).not.toBeInTheDocument();
  });

  it('leads with the specimen instead of fabricated social proof', () => {
    render(<LandingPage />);

    // No users yet, so testimonials or counts would have to be invented.
    expect(screen.queryByText(/testimonial|trusted by|users love/i)).toBeNull();

    expect(screen.getByText('Without Me')).toBeInTheDocument();
    expect(screen.getByText('Mr. Brightside')).toBeInTheDocument();
    // "rockstar" appears twice by design — once as a row, once in the caption
    // that explains why it is missing from the copy.
    expect(screen.getAllByText('rockstar')).toHaveLength(2);
  });

  it('teaches above the fold that a clean copy can be shorter', () => {
    render(<LandingPage />);

    // The single most misunderstood thing about the product.
    expect(screen.getByText('Not in the copy')).toBeInTheDocument();
    expect(screen.getByText(/no clean version exists/i)).toBeInTheDocument();
    expect(
      screen.getByText(/sometimes shorter than its source/i)
    ).toBeInTheDocument();
  });

  it('states the subscription requirement rather than burying it', () => {
    render(<LandingPage />);

    expect(
      screen.getByText(/needs an active apple music subscription/i)
    ).toBeInTheDocument();
  });

  it('renders every FAQ entry from the shared source', () => {
    render(<LandingPage />);

    for (const item of FAQ) {
      expect(screen.getByText(item.question)).toBeInTheDocument();
    }
  });

  it('greets a signed-in visitor with the dashboard, not a signup pitch', () => {
    render(<LandingPage signedIn email="someone@example.com" />);

    expect(
      screen.getByRole('link', { name: /go to your dashboard/i })
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

  it('links to the legal pages the footer promises', () => {
    render(<LandingPage />);

    expect(screen.getByRole('link', { name: /privacy/i })).toHaveAttribute(
      'href',
      '/privacy'
    );
    expect(screen.getByRole('link', { name: /terms/i })).toHaveAttribute(
      'href',
      '/terms'
    );
  });
});
