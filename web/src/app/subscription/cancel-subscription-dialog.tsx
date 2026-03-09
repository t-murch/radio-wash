'use client';

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';

interface CancelSubscriptionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
  periodEndDate?: string;
  isLoading?: boolean;
}

export function CancelSubscriptionDialog({
  open,
  onOpenChange,
  onConfirm,
  periodEndDate,
  isLoading,
}: CancelSubscriptionDialogProps) {
  const formattedDate = periodEndDate
    ? new Date(periodEndDate).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      })
    : null;

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Cancel Subscription?</AlertDialogTitle>
          <AlertDialogDescription>
            {formattedDate
              ? `You will retain access until the end of your billing period on ${formattedDate}. After that, automatic sync will be disabled.`
              : 'You will retain access until the end of your billing period. After that, automatic sync will be disabled.'}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep Subscription</AlertDialogCancel>
          <AlertDialogAction
            onClick={onConfirm}
            disabled={isLoading}
            className="bg-error text-error-foreground hover:bg-error-hover"
          >
            {isLoading ? 'Cancelling...' : 'Yes, Cancel Subscription'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
