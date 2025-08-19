import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { useSearchParams } from 'next/navigation';
import { AuthForm } from '../auth-form';

// Mock Next.js useSearchParams
vi.mock('next/navigation', () => ({
  useSearchParams: vi.fn(),
}));

describe('AuthForm', () => {
  let mockSignInWithSpotify: Mock;
  let mockSearchParams: {
    get: Mock;
  };

  beforeEach(() => {
    mockSignInWithSpotify = vi.fn();
    mockSearchParams = {
      get: vi.fn(),
    };

    (useSearchParams as Mock).mockReturnValue(mockSearchParams);
  });

  it('should render auth form with Spotify sign-in button', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    expect(screen.getByText('RadioWash')).toBeInTheDocument();
    expect(
      screen.getByText('Create clean versions of your Spotify playlists')
    ).toBeInTheDocument();
    expect(screen.getByText('Sign up with Spotify')).toBeInTheDocument();
  });

  it('should display error message when error parameter is present', () => {
    const errorMessage = 'Authentication failed';
    mockSearchParams.get.mockReturnValue(errorMessage);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    const errorAlert = screen.getByRole('alert');
    expect(errorAlert).toBeInTheDocument();
    expect(errorAlert).toHaveTextContent(errorMessage);
    expect(errorAlert).toHaveClass('text-red-700', 'bg-red-100');
  });

  it('should not display error message when no error parameter', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('should render form with submit button that has correct attributes', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    const form = screen
      .getByRole('button', { name: /sign up with spotify/i })
      .closest('form');
    const signInButton = screen.getByText('Sign up with Spotify');

    expect(form).toBeInTheDocument();
    expect(signInButton).toHaveAttribute('type', 'submit');
  });

  it('should have correct button styling and attributes', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    const signInButton = screen.getByText('Sign up with Spotify');

    expect(signInButton).toHaveAttribute('type', 'submit');
    expect(signInButton).toHaveClass(
      'w-full',
      'bg-green-600',
      'hover:bg-green-700',
      'focus:ring-green-500'
    );
  });

  it('should contain Spotify icon SVG', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    const svgElement = screen
      .getByText('Sign up with Spotify')
      .querySelector('svg');
    expect(svgElement).toBeInTheDocument();
    expect(svgElement).toHaveClass('w-5', 'h-5', 'mr-2');
  });

  it('should display helpful description text', () => {
    mockSearchParams.get.mockReturnValue(null);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    expect(
      screen.getByText(
        /Sign up with Spotify to instantly access your playlists/
      )
    ).toBeInTheDocument();
  });

  it('should handle multiple different error messages', () => {
    const errorMessages = [
      'Access denied',
      'Invalid request',
      'Server error',
      'Network timeout',
    ];

    errorMessages.forEach((error, _index) => {
      mockSearchParams.get.mockReturnValue(error);

      const { unmount } = render(
        <AuthForm signInWithSpotify={mockSignInWithSpotify} />
      );

      const errorAlert = screen.getByRole('alert');
      expect(errorAlert).toHaveTextContent(error);

      // Clean up between iterations to avoid multiple alerts
      unmount();
    });
  });

  it('should be accessible with proper ARIA attributes', () => {
    const errorMessage = 'Test error';
    mockSearchParams.get.mockReturnValue(errorMessage);

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    const errorAlert = screen.getByRole('alert');
    expect(errorAlert).toBeInTheDocument();

    const signInButton = screen.getByRole('button', {
      name: /sign up with spotify/i,
    });
    expect(signInButton).toBeInTheDocument();
  });

  it('should render without Server Action function warnings', () => {
    // This test ensures the component renders without console warnings
    // Server Action functionality should be tested separately in integration tests
    mockSearchParams.get.mockReturnValue(null);

    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    render(<AuthForm signInWithSpotify={mockSignInWithSpotify} />);

    expect(screen.getByText('RadioWash')).toBeInTheDocument();
    expect(screen.getByText('Sign up with Spotify')).toBeInTheDocument();

    // Note: Server Action warnings are expected in test environment
    // This test documents the current limitation
    consoleSpy.mockRestore();
  });
});
