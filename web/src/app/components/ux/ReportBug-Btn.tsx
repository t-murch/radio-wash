'use client';

import { MessageSquare } from 'lucide-react';
import { Button } from '../ui/button';
import { SentryErrorBoundary } from './SentryErrorBoundary';
import { useSentryFeedback } from '@/hooks/useSentryFeedback';

function FeedbackButton() {
  const { isAvailable, openFeedbackForm } = useSentryFeedback();

  if (!isAvailable) {
    return null;
  }

  return (
    <Button
      type="button"
      variant="ghost"
      className="w-full justify-start px-2 py-1.5 text-sm font-normal"
      onClick={openFeedbackForm}
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
  const { isAvailable, openFeedbackForm } = useSentryFeedback();

  if (!isAvailable) return null;

  return (
    <Button
      type="button"
      variant="outline"
      className="fixed bottom-4 right-4 z-50 group flex items-center gap-0 hover:gap-2
                 rounded-full px-3 py-2 shadow-lg transition-all duration-200
                 bg-card border-border hover:bg-accent"
      onClick={openFeedbackForm}
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
