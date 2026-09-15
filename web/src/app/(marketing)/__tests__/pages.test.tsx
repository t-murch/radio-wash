import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';

import HowItWorksPage from '../how-it-works/page';
import CleanPlaylistGuidePage from '../guides/clean-apple-music-playlist/page';
import AppleMusicCleanPlaylistPage from '../apple-music-clean-playlist/page';
import { MARKETING_ROUTES } from '@/lib/routes';
import { MANUAL_STEPS } from '@/lib/content/clean-playlist-guide';

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
  // Asserted on the whole rendered text, not per-text-node queries: a
  // forbidden phrase split across inline elements must not evade the guard.
  it.each([
    ['HowItWorksPage', HowItWorksPage],
    ['CleanPlaylistGuidePage', CleanPlaylistGuidePage],
  ])('%s stays inside the copy guardrails', (_name, Page) => {
    const { container } = render(<Page />);
    const text = container.textContent ?? '';

    expect(text).not.toMatch(/spotify/i);
    expect(text).not.toMatch(/coming soon|waitlist/i);
    expect(text).not.toMatch(/200 tracks|10 sync/i);
  });
});

describe('AppleMusicCleanPlaylistPage', () => {
  it('leads with the primary keyword and the replacement mechanic', () => {
    render(<AppleMusicCleanPlaylistPage />);

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: /a clean playlist app for apple music/i,
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /replacing, not just filtering/i })
    ).toBeInTheDocument();
  });

  it('is honest that tracks without a clean version are omitted', () => {
    render(<AppleMusicCleanPlaylistPage />);

    expect(
      screen.getByRole('heading', { name: /when there is no clean version/i })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/rather than swapped for a cover, a remix/i)
    ).toBeInTheDocument();
  });

  // The claim the whole page rests on: free, but capped. "No track limit" on
  // its own read as unlimited, which the 10-playlist plan cap contradicts.
  it('states the free-tier playlist cap, not just the track allowance', () => {
    render(<AppleMusicCleanPlaylistPage />);

    // Stated twice on purpose: in "What it costs" and again in the FAQ answer,
    // which is also what the FAQPage schema emits.
    expect(
      screen.getAllByText(/free for your first 10 clean playlists/i)
    ).toHaveLength(2);
  });

  it('links to how-it-works and the guide from the body copy', () => {
    render(<AppleMusicCleanPlaylistPage />);

    expect(
      screen.getByRole('link', { name: /how it works/i })
    ).toHaveAttribute('href', MARKETING_ROUTES.howItWorks);
    expect(
      screen.getByRole('link', { name: /clean-playlist guide/i })
    ).toHaveAttribute('href', MARKETING_ROUTES.cleanPlaylistGuide);
  });
});

describe('CleanPlaylistGuidePage manual steps', () => {
  // The steps feed both the rendered <ol> and the HowTo schema; rendering from
  // the same constant is what stops search results describing steps the page
  // no longer shows.
  it('renders every step from the shared constant', () => {
    render(<CleanPlaylistGuidePage />);

    for (const step of MANUAL_STEPS) {
      expect(screen.getByText(step.text)).toBeInTheDocument();
    }
  });
});
