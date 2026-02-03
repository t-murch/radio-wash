'use client';

import * as Sentry from '@sentry/nextjs';
import { useEffect, useState } from 'react';
import { Button } from '../ui/button';
import { SentryErrorBoundary } from './SentryErrorBoundary';

type FeedbackIntegration = ReturnType<typeof Sentry.getFeedback> | null;

function FeedbackButton() {
  const [feedback, setFeedback] = useState<FeedbackIntegration>(null);
  const [isAvailable, setIsAvailable] = useState(false);

  // Read `getFeedback` on the client only, to avoid hydration errors during server rendering
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

  const handleClick = async () => {
    if (!feedback) return;
    try {
      const form = await feedback.createForm();
      form.appendToDom();
      form.open();
    } catch (error) {
      console.warn('Failed to open Sentry feedback form:', error);
    }
  };

  // Don't render if Sentry feedback is not available
  if (!isAvailable) {
    return null;
  }

  return (
    <Button
      type="button"
      variant="ghost"
      className="w-full justify-start px-2 py-1.5 text-sm font-normal"
      onClick={handleClick}
    >
      Give me feedback
    </Button>
  );
}

export function AttachToFeedbackButton() {
  return (
    <SentryErrorBoundary fallback={null}>
      <FeedbackButton />
    </SentryErrorBoundary>
  );
}
