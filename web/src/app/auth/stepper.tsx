import { Check } from 'lucide-react';

import { cn } from '@/lib/utils';

export type OnboardingStep = 1 | 2 | 3;

const STEPS = [
  { n: 1 as const, label: 'Sign in', short: 'Sign in' },
  { n: 2 as const, label: 'Connect Apple Music', short: 'Apple Music' },
  { n: 3 as const, label: 'First playlist', short: 'Playlist' },
];

/**
 * The three-step spine shared by sign-in and onboarding.
 *
 * It exists because signing in and granting music access are genuinely two
 * different permissions, and a user who is not told that reads Apple's prompt as
 * an unexplained second login. Showing the whole sequence up front makes the
 * second step expected rather than suspicious.
 */
export function Stepper({
  current,
  className,
}: {
  current: OnboardingStep;
  className?: string;
}) {
  return (
    <nav aria-label="Progress" className={cn('w-full', className)}>
      <ol className="flex items-center gap-2 text-xs sm:gap-3 sm:text-sm">
        {STEPS.map((step, i) => {
          const done = step.n < current;
          const active = step.n === current;

          return (
            <li key={step.n} className="flex min-w-0 items-center gap-2 sm:gap-3">
              <span
                className={cn(
                  'flex size-5 shrink-0 items-center justify-center rounded-full border text-[11px] font-medium',
                  done && 'border-primary bg-primary text-primary-foreground',
                  active && 'border-primary text-primary',
                  !done && !active && 'border-border text-muted-foreground'
                )}
                aria-hidden="true"
              >
                {done ? <Check className="size-3" /> : step.n}
              </span>

              <span
                className={cn(
                  'truncate',
                  active ? 'font-medium text-foreground' : 'text-muted-foreground'
                )}
                aria-current={active ? 'step' : undefined}
              >
                {/* The full label needs room; short form keeps three steps on one
                    line on a phone rather than wrapping into a stack. */}
                <span className="hidden sm:inline">{step.label}</span>
                <span className="sm:hidden">{step.short}</span>
                <span className="sr-only">
                  {/* Whitespace, not '': an empty string renders no text node,
                      and inserting one later crashes if Chrome Translate has
                      rewrapped this span's children. AT trims the lone space. */}
                  {done ? ' (completed)' : active ? ' (current step)' : ' '}
                </span>
              </span>

              {i < STEPS.length - 1 && (
                <span
                  aria-hidden="true"
                  className="h-px w-3 shrink-0 bg-border sm:w-6"
                />
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
