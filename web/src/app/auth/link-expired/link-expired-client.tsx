'use client';

import { useState, useTransition } from 'react';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';

import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import type { MagicLinkState } from '../actions';

export function LinkExpired({
  sendMagicLink,
}: {
  sendMagicLink: (
    prevState: MagicLinkState,
    formData: FormData
  ) => Promise<MagicLinkState>;
  signInWithApple: () => Promise<void>;
  signInWithGoogle: () => Promise<void>;
}) {
  const [state, setState] = useState<MagicLinkState>({ status: 'idle' });
  const [isPending, startTransition] = useTransition();

  // React 18 does not support a function-valued form `action`; submit normally.
  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const formData = new FormData(event.currentTarget);
    startTransition(async () => {
      setState(await sendMagicLink(state, formData));
    });
  };

  if (state.status === 'sent' && state.email) {
    return (
      <div className="w-full max-w-md space-y-6">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          A fresh link is on its way to {state.email}
        </h1>
        <p className="text-muted-foreground">
          This one works once and expires after 15 minutes, same as the last.
        </p>
      </div>
    );
  }

  const fieldError = state.status === 'error' ? state.message : undefined;

  return (
    <div className="w-full max-w-md space-y-8">
      <div className="space-y-3">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          That link has already done its job
        </h1>
        <p className="text-muted-foreground">
          Sign-in links work once and expire after 15 minutes — this one was used
          or sat too long. Nothing&apos;s wrong with your account; a fresh one
          fixes it.
        </p>
      </div>

      {/*
        The design called for "It goes to <email> — no need to retype it", but a
        failed link leaves no session and no address to recover: the token hash is
        all we were given, and it did not verify. Asking once is honest; claiming
        to remember an address we never had would not be.
      */}
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="email">Email address</Label>
          <Input
            id="email"
            name="email"
            type="email"
            autoComplete="email"
            required
            placeholder="you@example.com"
            aria-invalid={fieldError ? 'true' : undefined}
            aria-describedby={fieldError ? 'email-error' : undefined}
          />
          {fieldError && (
            <p id="email-error" role="alert" className="text-sm text-error">
              {fieldError}
            </p>
          )}
        </div>

        <Button type="submit" className="w-full" disabled={isPending}>
          {isPending && <Loader2 className="size-4 animate-spin" />}
          {isPending ? 'Sending…' : 'Email me a new link'}
        </Button>
      </form>

      <Alert>
        <AlertDescription>
          Prefer not to wait for email?{' '}
          <Link
            href="/auth"
            className="text-primary underline underline-offset-4 hover:text-brand-hover"
          >
            Sign in with Apple or Google instead
          </Link>
          .
        </AlertDescription>
      </Alert>
    </div>
  );
}
