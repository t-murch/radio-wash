'use client';

import * as Sentry from '@sentry/nextjs';
import { useEffect, useState, useCallback } from 'react';

type FeedbackIntegration = ReturnType<typeof Sentry.getFeedback>;

interface UseSentryFeedbackResult {
  isAvailable: boolean;
  openFeedbackForm: () => Promise<void>;
}

export function useSentryFeedback(): UseSentryFeedbackResult {
  const [feedback, setFeedback] = useState<FeedbackIntegration | null>(null);
  const [isAvailable, setIsAvailable] = useState(false);

  useEffect(() => {
    try {
      const feedbackIntegration = Sentry.getFeedback();
      if (feedbackIntegration) {
        setFeedback(feedbackIntegration);
        setIsAvailable(true);
      }
    } catch (error) {
      console.warn('Sentry feedback integration not available:', error);
      setIsAvailable(false);
    }
  }, []);

  const openFeedbackForm = useCallback(async () => {
    if (!feedback) return;
    try {
      const form = await feedback.createForm();
      form.appendToDom();
      form.open();
    } catch (error) {
      console.warn('Failed to open Sentry feedback form:', error);
    }
  }, [feedback]);

  return { isAvailable, openFeedbackForm };
}
