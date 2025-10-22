'use client';

import * as Sentry from '@sentry/nextjs';
import { useEffect, useRef, useState } from 'react';
import { Button } from '../ui/button';
import { SentryErrorBoundary } from './SentryErrorBoundary';

type FeedbackIntegration = ReturnType<typeof Sentry.getFeedback> | null;

function FeedbackButton() {
  const [feedback, setFeedback] = useState<FeedbackIntegration>(null);
  const [isAvailable, setIsAvailable] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);

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

  useEffect(() => {
    if (feedback && buttonRef.current && isAvailable) {
      try {
        const unsubscribe = feedback.attachTo(buttonRef.current);
        return unsubscribe;
      } catch (error) {
        console.warn('Failed to attach Sentry feedback:', error);
        setIsAvailable(false);
      }
    }
    return () => {};
  }, [feedback, isAvailable]);

  // Don't render if Sentry feedback is not available
  if (!isAvailable) {
    return null;
  }

  return (
    <Button
      ref={buttonRef}
      type="button"
      variant="ghost"
      className="w-full justify-start px-2 py-1.5 text-sm font-normal"
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
