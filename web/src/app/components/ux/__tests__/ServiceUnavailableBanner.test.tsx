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

  it('should display Coming Soon section for Apple Music', () => {
    render(<ServiceUnavailableBanner />);

    expect(screen.getByText(/coming soon/i)).toBeInTheDocument();
    expect(screen.getByText(/apple music support/i)).toBeInTheDocument();
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

  it('should use warning semantic color classes', () => {
    render(<ServiceUnavailableBanner />);

    const banner = screen.getByRole('alert');
    expect(banner.className).toContain('bg-warning/10');
    expect(banner.className).toContain('border-warning/20');
  });
});
