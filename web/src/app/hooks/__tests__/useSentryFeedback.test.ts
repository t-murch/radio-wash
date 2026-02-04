import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import * as Sentry from '@sentry/nextjs';
import { useSentryFeedback } from '../useSentryFeedback';

vi.mock('@sentry/nextjs', () => ({
  getFeedback: vi.fn(),
}));

describe('useSentryFeedback', () => {
  let mockFeedbackIntegration: {
    createForm: Mock;
  };
  let mockForm: {
    appendToDom: Mock;
    open: Mock;
  };

  beforeEach(() => {
    vi.clearAllMocks();

    mockForm = {
      appendToDom: vi.fn(),
      open: vi.fn(),
    };

    mockFeedbackIntegration = {
      createForm: vi.fn().mockResolvedValue(mockForm),
    };
  });

  it('should return isAvailable: false initially when Sentry feedback is not available', async () => {
    (Sentry.getFeedback as Mock).mockReturnValue(null);

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(false);
    });
  });

  it('should return isAvailable: true when Sentry feedback is available', async () => {
    (Sentry.getFeedback as Mock).mockReturnValue(mockFeedbackIntegration);

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(true);
    });
  });

  it('should handle Sentry feedback initialization error gracefully', async () => {
    const consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    (Sentry.getFeedback as Mock).mockImplementation(() => {
      throw new Error('Sentry not initialized');
    });

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(false);
      expect(consoleWarnSpy).toHaveBeenCalledWith(
        'Sentry feedback integration not available:',
        expect.any(Error)
      );
    });

    consoleWarnSpy.mockRestore();
  });

  it('should create and open form when openFeedbackForm is called', async () => {
    (Sentry.getFeedback as Mock).mockReturnValue(mockFeedbackIntegration);

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(true);
    });

    await act(async () => {
      await result.current.openFeedbackForm();
    });

    expect(mockFeedbackIntegration.createForm).toHaveBeenCalled();
    expect(mockForm.appendToDom).toHaveBeenCalled();
    expect(mockForm.open).toHaveBeenCalled();
  });

  it('should not throw when openFeedbackForm is called but feedback is not available', async () => {
    (Sentry.getFeedback as Mock).mockReturnValue(null);

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(false);
    });

    await act(async () => {
      await result.current.openFeedbackForm();
    });

    expect(mockFeedbackIntegration.createForm).not.toHaveBeenCalled();
  });

  it('should handle form creation error gracefully', async () => {
    const consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    (Sentry.getFeedback as Mock).mockReturnValue({
      createForm: vi.fn().mockRejectedValue(new Error('Form creation failed')),
    });

    const { result } = renderHook(() => useSentryFeedback());

    await waitFor(() => {
      expect(result.current.isAvailable).toBe(true);
    });

    await act(async () => {
      await result.current.openFeedbackForm();
    });

    expect(consoleWarnSpy).toHaveBeenCalledWith(
      'Failed to open Sentry feedback form:',
      expect.any(Error)
    );

    consoleWarnSpy.mockRestore();
  });
});
