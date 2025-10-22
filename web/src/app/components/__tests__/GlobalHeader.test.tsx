import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { useRouter } from 'next/navigation';
import type { User as ApiUser } from '@/services/api';

import { GlobalHeader } from '../GlobalHeader';
import * as SentryUserContext from '@/lib/sentry-user-context';

// Mock Next.js router
vi.mock('next/navigation', () => ({
  useRouter: vi.fn(),
}));

// Mock Supabase client
const mockSignOut = vi.fn();
vi.mock('@/lib/supabase/client', () => ({
  createClient: () => ({
    auth: {
      signOut: mockSignOut,
    },
  }),
}));

// Mock Sentry user context
vi.mock('@/lib/sentry-user-context', () => ({
  setSentryUser: vi.fn(),
  clearSentryUser: vi.fn(),
}));

// Mock AttachToFeedbackButton
vi.mock('../ux/ReportBug-Btn', () => ({
  AttachToFeedbackButton: () => <button>Give me feedback</button>,
}));

// Mock Next.js Image component
vi.mock('next/image', () => ({
  default: ({ src, alt, ...props }: any) => <img src={src} alt={alt} {...props} />,
}));

// Mock Next.js Link component
vi.mock('next/link', () => ({
  default: ({ href, children, ...props }: any) => <a href={href} {...props}>{children}</a>,
}));

describe('GlobalHeader', () => {
  const mockPush = vi.fn();
  
  beforeEach(() => {
    vi.clearAllMocks();
    (useRouter as any).mockReturnValue({
      push: mockPush,
    });
  });

  const mockUser: ApiUser = {
    id: 'user-123',
    email: 'test@example.com',
    displayName: 'Test User',
    profileImageUrl: 'https://example.com/avatar.jpg',
  };

  describe('User Context Integration', () => {
    it('should set Sentry user context when user is provided', () => {
      render(<GlobalHeader user={mockUser} />);

      expect(SentryUserContext.setSentryUser).toHaveBeenCalledWith(mockUser);
    });

    it('should set Sentry user context to null when no user provided', () => {
      render(<GlobalHeader user={null} />);

      expect(SentryUserContext.setSentryUser).toHaveBeenCalledWith(null);
    });

    it('should update Sentry user context when user changes', () => {
      const { rerender } = render(<GlobalHeader user={null} />);
      
      expect(SentryUserContext.setSentryUser).toHaveBeenCalledWith(null);

      rerender(<GlobalHeader user={mockUser} />);
      
      expect(SentryUserContext.setSentryUser).toHaveBeenCalledWith(mockUser);
    });
  });

  describe('Feedback Widget Visibility', () => {
    it('should show feedback button for logged-in users', () => {
      render(<GlobalHeader user={mockUser} />);

      // Click on user dropdown to reveal menu
      const userButton = screen.getByRole('button', { name: /User Profile/i });
      fireEvent.click(userButton);

      expect(screen.getByText('Give me feedback')).toBeInTheDocument();
    });

    it('should not show feedback button for non-logged-in users', () => {
      render(<GlobalHeader user={null} />);

      // Should show Sign In button instead of user dropdown
      expect(screen.getByText('Sign In')).toBeInTheDocument();
      expect(screen.queryByText('Give me feedback')).not.toBeInTheDocument();
    });

    it('should show feedback button in dropdown menu alongside other menu items', () => {
      render(<GlobalHeader user={mockUser} />);

      // Click on user dropdown
      const userButton = screen.getByRole('button', { name: /User Profile/i });
      fireEvent.click(userButton);

      // Verify all expected menu items are present
      expect(screen.getByText('Test User')).toBeInTheDocument();
      expect(screen.getByText('test@example.com')).toBeInTheDocument();
      expect(screen.getByText('Dashboard')).toBeInTheDocument();
      expect(screen.getByText('Give me feedback')).toBeInTheDocument();
      expect(screen.getByText('Sign out')).toBeInTheDocument();
    });
  });

  describe('User Authentication Flow', () => {
    it('should clear Sentry user context on sign out', async () => {
      mockSignOut.mockResolvedValue({});
      
      render(<GlobalHeader user={mockUser} />);

      // Click on user dropdown
      const userButton = screen.getByRole('button', { name: /User Profile/i });
      fireEvent.click(userButton);

      // Click sign out
      const signOutButton = screen.getByText('Sign out');
      fireEvent.click(signOutButton);

      await waitFor(() => {
        expect(SentryUserContext.clearSentryUser).toHaveBeenCalled();
        expect(mockSignOut).toHaveBeenCalled();
        expect(mockPush).toHaveBeenCalledWith('/');
      });
    });

    it('should navigate to auth page when sign in clicked', () => {
      render(<GlobalHeader user={null} />);

      const signInButton = screen.getByText('Sign In');
      fireEvent.click(signInButton);

      expect(mockPush).toHaveBeenCalledWith('/auth');
    });
  });

  describe('User Profile Display', () => {
    it('should display user profile image when available', () => {
      render(<GlobalHeader user={mockUser} />);

      const profileImage = screen.getByAltText('User Profile');
      expect(profileImage).toHaveAttribute('src', mockUser.profileImageUrl!);
    });

    it('should display user icon when no profile image available', () => {
      const userWithoutImage: ApiUser = {
        ...mockUser,
        profileImageUrl: null,
      };

      render(<GlobalHeader user={userWithoutImage} />);

      // Should not have an img element, should use the User icon instead
      expect(screen.queryByAltText('User Profile')).not.toBeInTheDocument();
    });

    it('should display user information in dropdown', () => {
      render(<GlobalHeader user={mockUser} />);

      // Click on user dropdown
      const userButton = screen.getByRole('button', { name: /User Profile/i });
      fireEvent.click(userButton);

      expect(screen.getByText('Test User')).toBeInTheDocument();
      expect(screen.getByText('test@example.com')).toBeInTheDocument();
    });
  });

  describe('Navigation Props', () => {
    it('should show back button when showBackButton is true', () => {
      render(
        <GlobalHeader 
          user={mockUser} 
          showBackButton={true}
          backButtonHref="/custom"
          backButtonLabel="Custom Back"
        />
      );

      expect(screen.getByText('← Custom Back')).toBeInTheDocument();
      expect(screen.getByText('← Custom Back').closest('a')).toHaveAttribute('href', '/custom');
    });

    it('should not show back button by default', () => {
      render(<GlobalHeader user={mockUser} />);

      expect(screen.queryByText(/Back/)).not.toBeInTheDocument();
    });

    it('should use default back button props when not specified', () => {
      render(<GlobalHeader user={mockUser} showBackButton={true} />);

      expect(screen.getByText('← Back to Dashboard')).toBeInTheDocument();
      expect(screen.getByText('← Back to Dashboard').closest('a')).toHaveAttribute('href', '/dashboard');
    });
  });
});