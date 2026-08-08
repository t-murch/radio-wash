import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { ServiceUnavailableBanner } from '../ServiceUnavailableBanner';

describe('ServiceUnavailableBanner', () => {
  it('should render the banner with correct heading', () => {
    render(<ServiceUnavailableBanner />);

    expect(
      screen.getByRole('heading', {
        name: /radiowash is temporarily unavailable/i,
      })
    ).toBeInTheDocument();
  });

  it('names the outage without blaming the user, and reassures about their data', () => {
    render(<ServiceUnavailableBanner />);

    expect(
      screen.getByText(/apple music isn't responding right now/i)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/your playlists are unaffected/i)
    ).toBeInTheDocument();
  });

  it('no longer blames Spotify for the outage', () => {
    render(<ServiceUnavailableBanner />);
    expect(screen.queryByText(/spotify/i)).not.toBeInTheDocument();
  });

  it('should no longer advertise Apple Music as coming soon (it shipped)', () => {
    render(<ServiceUnavailableBanner />);

    expect(screen.queryByText(/coming soon/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/apple music support/i)).not.toBeInTheDocument();
  });

  it('should have role="alert" for accessibility', () => {
    render(<ServiceUnavailableBanner />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('should have correct id for aria-describedby references', () => {
    render(<ServiceUnavailableBanner />);

    const banner = screen.getByRole('alert');
    expect(banner).toHaveAttribute('id', 'service-unavailable-banner');
  });

});
