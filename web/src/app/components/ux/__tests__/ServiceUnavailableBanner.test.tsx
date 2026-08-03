import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { ServiceUnavailableBanner } from '../ServiceUnavailableBanner';

describe('ServiceUnavailableBanner', () => {
  it('should render the banner with correct heading', () => {
    render(<ServiceUnavailableBanner />);

    expect(
      screen.getByRole('heading', { name: /service temporarily unavailable/i })
    ).toBeInTheDocument();
  });

  it('should display Spotify API limitation message', () => {
    render(<ServiceUnavailableBanner />);

    expect(
      screen.getByText(/due to spotify api limitations/i)
    ).toBeInTheDocument();
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
