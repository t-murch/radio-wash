import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';

// Mock Sentry
vi.mock('@sentry/nextjs', () => ({
  getFeedback: vi.fn(),
}));

import { AttachToFeedbackButton } from '../ReportBug-Btn';
import * as Sentry from '@sentry/nextjs';

const mockAttachTo = vi.fn();

// Mock the SentryErrorBoundary to focus on testing the FeedbackButton logic
vi.mock('../SentryErrorBoundary', () => ({
  SentryErrorBoundary: ({ children }: { children: React.ReactNode }) => children,
}));

describe('AttachToFeedbackButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset console.warn mock
    vi.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should render feedback button when Sentry feedback is available', async () => {
    // Mock successful Sentry feedback integration
    const mockFeedbackIntegration = {
      attachTo: mockAttachTo.mockReturnValue(() => {}), // Mock unsubscribe function
    };
    vi.mocked(Sentry.getFeedback).mockReturnValue(mockFeedbackIntegration);

    render(<AttachToFeedbackButton />);

    // Wait for useEffect to run
    await screen.findByText('Give me feedback');
    
    expect(screen.getByText('Give me feedback')).toBeInTheDocument();
    expect(Sentry.getFeedback).toHaveBeenCalled();
  });

  it('should not render feedback button when Sentry feedback is not available', () => {
    // Mock Sentry.getFeedback returning null
    vi.mocked(Sentry.getFeedback).mockReturnValue(null);

    render(<AttachToFeedbackButton />);

    expect(screen.queryByText('Give me feedback')).not.toBeInTheDocument();
    expect(Sentry.getFeedback).toHaveBeenCalled();
  });

  it('should handle Sentry.getFeedback throwing an error gracefully', () => {
    // Mock Sentry.getFeedback throwing an error
    vi.mocked(Sentry.getFeedback).mockImplementation(() => {
      throw new Error('Sentry not available');
    });

    render(<AttachToFeedbackButton />);

    expect(screen.queryByText('Give me feedback')).not.toBeInTheDocument();
    expect(console.warn).toHaveBeenCalledWith(
      'Sentry feedback integration not available:',
      expect.any(Error)
    );
  });

  it('should handle attachTo method failing gracefully', async () => {
    // Mock Sentry feedback integration that fails during attach
    const mockFeedbackIntegration = {
      attachTo: mockAttachTo.mockImplementation(() => {
        throw new Error('Attach failed');
      }),
    };
    vi.mocked(Sentry.getFeedback).mockReturnValue(mockFeedbackIntegration);

    render(<AttachToFeedbackButton />);

    // Wait for initial render
    await screen.findByText('Give me feedback');
    
    // Button should initially render but then disappear due to attach failure
    expect(console.warn).toHaveBeenCalledWith(
      'Failed to attach Sentry feedback:',
      expect.any(Error)
    );
  });

  it('should have correct button styling and accessibility', async () => {
    const mockFeedbackIntegration = {
      attachTo: mockAttachTo.mockReturnValue(() => {}),
    };
    vi.mocked(Sentry.getFeedback).mockReturnValue(mockFeedbackIntegration);

    render(<AttachToFeedbackButton />);

    const button = await screen.findByText('Give me feedback');
    
    expect(button).toHaveAttribute('type', 'button');
    expect(button).toHaveClass('w-full', 'justify-start', 'px-2', 'py-1.5', 'text-sm', 'font-normal');
  });
});