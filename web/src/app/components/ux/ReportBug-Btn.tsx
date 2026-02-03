'use client';

import * as Sentry from '@sentry/nextjs';
import { MessageSquare } from 'lucide-react';
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

function FloatingFeedback() {
  const [feedback, setFeedback] = useState<FeedbackIntegration>(null);
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

  if (!isAvailable) return null;

  return (
    <Button
      type="button"
      variant="outline"
      className="fixed bottom-4 right-4 z-50 group flex items-center gap-0 hover:gap-2
                 rounded-full px-3 py-2 shadow-lg transition-all duration-200
                 bg-card border-border hover:bg-accent"
      onClick={handleClick}
      aria-label="Send feedback"
    >
      <MessageSquare className="h-5 w-5" />
      <span className="max-w-0 overflow-hidden whitespace-nowrap group-hover:max-w-xs transition-all duration-200">
        Feedback
      </span>
    </Button>
  );
}

export function FloatingFeedbackButton() {
  return (
    <SentryErrorBoundary fallback={null}>
      <FloatingFeedback />
    </SentryErrorBoundary>
  );
}
