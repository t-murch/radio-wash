import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';

import HowItWorksPage from '../how-it-works/page';
import CleanPlaylistGuidePage from '../guides/clean-apple-music-playlist/page';
import { MARKETING_ROUTES } from '@/lib/routes';
import sitemap from '../../sitemap';

describe('HowItWorksPage', () => {
  it('explains the matching pipeline and the shorter-copy consequence', () => {
    render(<HowItWorksPage />);

    expect(
      screen.getByRole('heading', { level: 1, name: /how radiowash works/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /how a clean match is found/i })
    ).toBeInTheDocument();
    // The honest core of the product: omission, never substitution.
    expect(
      screen.getByText(/omitted from the copy rather than swapped/i)
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /why a clean copy can be shorter/i })
    ).toBeInTheDocument();
  });

  it('states what Auto-Sync can and cannot do', () => {
    render(<HowItWorksPage />);

    expect(screen.getByText(/auto-sync only adds/i)).toBeInTheDocument();
    expect(screen.getByText(/\$5\s*per month/i)).toBeInTheDocument();
  });

  it('sends visitors to sign-in, not a waitlist', () => {
    render(<HowItWorksPage />);

    expect(
      screen.getByRole('link', { name: /make a clean copy/i })
    ).toHaveAttribute('href', '/auth');
  });
});

describe('CleanPlaylistGuidePage', () => {
  it('covers the manual method as real steps, not a strawman', () => {
    render(<CleanPlaylistGuidePage />);

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: /how to make an apple music playlist clean/i,
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /the manual way, in the music app/i })
    ).toBeInTheDocument();
    // The manual route gets honest credit, including when it's fine.
    expect(
      screen.getByText(/for a short playlist it can be done in a few minutes/i)
    ).toBeInTheDocument();
  });

  it('links to the how-it-works page for the matching detail', () => {
    render(<CleanPlaylistGuidePage />);

    expect(
      screen.getByRole('link', { name: /how-it-works page/i })
    ).toHaveAttribute('href', MARKETING_ROUTES.howItWorks);
  });

  it('sends visitors to sign-in', () => {
    render(<CleanPlaylistGuidePage />);

    expect(
      screen.getByRole('link', { name: /make a clean copy/i })
    ).toHaveAttribute('href', '/auth');
  });
});

describe('marketing copy guardrails', () => {
  // The brief forbids fake urgency and competitor talk, and constraints.md
  // flags the plan-limit numbers as advertised-but-not-enforced — none of it
  // may appear in published copy.
  it.each([
    ['HowItWorksPage', HowItWorksPage],
    ['CleanPlaylistGuidePage', CleanPlaylistGuidePage],
  ])('%s stays inside the copy guardrails', (_name, Page) => {
    render(<Page />);

    expect(screen.queryByText(/spotify/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/coming soon|waitlist/i)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/200 tracks|10 sync/i)
    ).not.toBeInTheDocument();
  });
});

describe('sitemap', () => {
  it('lists both new marketing pages', () => {
    const urls = sitemap().map((entry) => entry.url);

    expect(urls).toContain(
      `https://radiowash.com${MARKETING_ROUTES.howItWorks}`
    );
    expect(urls).toContain(
      `https://radiowash.com${MARKETING_ROUTES.cleanPlaylistGuide}`
    );
  });
});
