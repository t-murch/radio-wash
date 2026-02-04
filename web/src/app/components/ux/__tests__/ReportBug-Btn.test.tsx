import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { FloatingFeedbackButton, AttachToFeedbackButton } from '../ReportBug-Btn';
import { useSentryFeedback } from '@/hooks/useSentryFeedback';

vi.mock('@/hooks/useSentryFeedback', () => ({
  useSentryFeedback: vi.fn(),
}));

vi.mock('@sentry/nextjs', () => ({
  withScope: vi.fn(),
  captureException: vi.fn(),
}));

describe('FloatingFeedbackButton', () => {
  let mockOpenFeedbackForm: Mock;

  beforeEach(() => {
    vi.clearAllMocks();
    mockOpenFeedbackForm = vi.fn();
  });

  it('should render floating button when feedback is available', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<FloatingFeedbackButton />);

    const button = screen.getByRole('button', { name: /send feedback/i });
    expect(button).toBeInTheDocument();
  });

  it('should not render when feedback is unavailable', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: false,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<FloatingFeedbackButton />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('should have correct aria-label for accessibility', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<FloatingFeedbackButton />);

    const button = screen.getByRole('button');
    expect(button).toHaveAttribute('aria-label', 'Send feedback');
  });

  it('should call openFeedbackForm when clicked', async () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<FloatingFeedbackButton />);

    const button = screen.getByRole('button');
    fireEvent.click(button);

    await waitFor(() => {
      expect(mockOpenFeedbackForm).toHaveBeenCalled();
    });
  });

  it('should display MessageSquare icon', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<FloatingFeedbackButton />);

    const button = screen.getByRole('button');
    expect(button.querySelector('svg')).toBeInTheDocument();
  });
});

describe('AttachToFeedbackButton', () => {
  let mockOpenFeedbackForm: Mock;

  beforeEach(() => {
    vi.clearAllMocks();
    mockOpenFeedbackForm = vi.fn();
  });

  it('should render feedback button when available', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<AttachToFeedbackButton />);

    const button = screen.getByRole('button', { name: /give me feedback/i });
    expect(button).toBeInTheDocument();
  });

  it('should not render when feedback is unavailable', () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: false,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<AttachToFeedbackButton />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('should call openFeedbackForm when clicked', async () => {
    (useSentryFeedback as Mock).mockReturnValue({
      isAvailable: true,
      openFeedbackForm: mockOpenFeedbackForm,
    });

    render(<AttachToFeedbackButton />);

    const button = screen.getByRole('button');
    fireEvent.click(button);

    await waitFor(() => {
      expect(mockOpenFeedbackForm).toHaveBeenCalled();
    });
  });
});
