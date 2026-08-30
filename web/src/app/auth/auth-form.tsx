'use client';

import { useEffect, useState, useTransition } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { trackMagicLinkRequested } from '@/lib/analytics';

import { createClient } from '@/lib/supabase/client';

import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import type { MagicLinkState } from './actions';
import { AppleMark, GoogleMark } from './provider-marks';
import { Stepper } from './stepper';

export function AuthForm({
  sendMagicLink,
  signInWithApple,
  signInWithGoogle,
}: {
  sendMagicLink: (
    prevState: MagicLinkState,
    formData: FormData
  ) => Promise<MagicLinkState>;
  signInWithApple: () => Promise<void>;
  signInWithGoogle: () => Promise<void>;
}) {
  const searchParams = useSearchParams();
  const urlError = searchParams.get('error');

  const [state, setState] = useState<MagicLinkState>({ status: 'idle' });
  const [isPending, startTransition] = useTransition();

  // React 18 supports neither useFormState nor a function-valued `action`, so the
  // server action is invoked from a plain onSubmit handler inside a transition.
  const submitWith = (formData: FormData) => {
    startTransition(async () => {
      const nextState = await sendMagicLink(state, formData);
      if (nextState.status === 'sent') {
        trackMagicLinkRequested();
      }
      setState(nextState);
    });
  };

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    submitWith(new FormData(event.currentTarget));
  };

  if (state.status === 'sent' && state.email) {
    return (
      <CheckInbox
        email={state.email}
        onChangeAddress={() => setState({ status: 'idle' })}
        onResend={() => {
          const data = new FormData();
          data.set('email', state.email!);
          submitWith(data);
        }}
        isResending={isPending}
      />
    );
  }

  const fieldError = state.status === 'error' ? state.message : undefined;

  return (
    <div className="w-full max-w-md space-y-8">
      <Stepper current={1} />

      <div className="space-y-2">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          Sign in with email
        </h1>
        <p className="text-muted-foreground">
          We&apos;ll email you a link that signs you in.
        </p>
      </div>

      {urlError && (
        <Alert variant="error">
          <AlertDescription>{urlError}</AlertDescription>
        </Alert>
      )}

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
            defaultValue={state.email}
            aria-invalid={fieldError ? 'true' : undefined}
            aria-describedby={fieldError ? 'email-error' : undefined}
          />
          {fieldError && (
            <p id="email-error" role="alert" className="text-sm text-error">
              <span>{fieldError}</span>
            </p>
          )}
        </div>

        <Button type="submit" className="w-full" disabled={isPending}>
          {isPending && <Loader2 className="size-4 animate-spin" />}
          {/* Bare text kept inside a span: Chrome Translate rewraps loose text
              nodes in <font> tags, and React then crashes inserting the spinner
              next to a text node it no longer owns (Sentry JAVASCRIPT-NEXTJS-22). */}
          <span>{isPending ? 'Sending…' : 'Send sign-in link'}</span>
        </Button>
      </form>

      <p className="text-sm text-muted-foreground">
        No password — not now, not later. The link is the whole sign-in.
      </p>

      <div className="flex items-center gap-4">
        <Separator className="flex-1" />
        <span className="text-xs uppercase tracking-wide text-muted-foreground">
          or continue with
        </span>
        <Separator className="flex-1" />
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <form action={signInWithApple}>
          <Button type="submit" variant="outline" className="w-full">
            <AppleMark />
            Apple
          </Button>
        </form>
        <form action={signInWithGoogle}>
          <Button type="submit" variant="outline" className="w-full">
            <GoogleMark />
            Google
          </Button>
        </form>
      </div>
    </div>
  );
}

function CheckInbox({
  email,
  onChangeAddress,
  onResend,
  isResending,
}: {
  email: string;
  onChangeAddress: () => void;
  onResend: () => void;
  isResending: boolean;
}) {
  const signedInElsewhere = useSessionWatch();

  if (signedInElsewhere) {
    return <SignedInElsewhere />;
  }

  return (
    <div className="w-full max-w-md space-y-8">
      <Stepper current={1} />

      <div className="space-y-2">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          <span>A sign-in link is on its way to </span>
          {/* The address must survive machine translation verbatim. */}
          <span translate="no" className="notranslate">
            {email}
          </span>
        </h1>
        <p className="text-muted-foreground">
          <span>Wrong address?</span>{' '}
          <button
            type="button"
            onClick={onChangeAddress}
            className="text-primary underline underline-offset-4 hover:text-brand-hover"
          >
            Change it
          </button>
        </p>
      </div>

      {/*
        Both notes are rendered and toggled by viewport rather than by sniffing the
        user agent, which is unreliable. Each describes what actually happens on
        that device, and the desktop one is a promise the session poller keeps.
      */}
      <div className="rounded-md border border-border bg-card p-4 text-sm text-muted-foreground">
        <span className="hidden sm:inline">
          Opening the link on your phone instead? That works — this page checks
          for your sign-in and carries on here by itself.
        </span>
        <span className="sm:hidden">
          Opening the link on this phone? Your mail app brings you straight back
          signed in.
        </span>
      </div>

      <ResendButton onResend={onResend} isResending={isResending} />

      <p className="text-sm text-muted-foreground">
        Nothing arriving? Check spam. The link works once and expires after 15
        minutes.
      </p>
    </div>
  );
}

/**
 * Watches for a session appearing while this tab sits on the check-inbox screen.
 *
 * This is what makes the device-switch case work rather than being a dead end:
 * the link signs in whichever device opens it, so someone who started on a
 * laptop and tapped the link on their phone would otherwise be stranded on a
 * page waiting forever. Supabase writes the session to storage and broadcasts
 * an auth event, so this tab notices and moves itself along.
 *
 * The check-inbox copy promises this behaviour in advance, and the screen it
 * leads to confirms it happened — no fake progress, because the page really is
 * watching.
 */
function useSessionWatch() {
  const [signedIn, setSignedIn] = useState(false);

  useEffect(() => {
    const supabase = createClient();
    let cancelled = false;

    // Covers the same-tab case and any session already established before mount.
    supabase.auth.getSession().then(({ data }) => {
      if (!cancelled && data.session) setSignedIn(true);
    });

    const {
      data: { subscription },
    } = supabase.auth.onAuthStateChange((_event, session) => {
      if (session) setSignedIn(true);
    });

    return () => {
      cancelled = true;
      subscription.unsubscribe();
    };
  }, []);

  return signedIn;
}

function SignedInElsewhere() {
  const router = useRouter();

  return (
    <div className="w-full max-w-md space-y-8">
      <Stepper current={1} />

      <div className="space-y-2">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          You opened the link on your phone — signed in here too
        </h1>
        <p className="text-muted-foreground">
          This page noticed your sign-in, just as it said it would. Carrying on
          here — connecting Apple Music is next.
        </p>
      </div>

      <Button onClick={() => router.push('/onboarding')}>Continue now</Button>
    </div>
  );
}

const RESEND_COOLDOWN_SECONDS = 24;

function ResendButton({
  onResend,
  isResending,
}: {
  onResend: () => void;
  isResending: boolean;
}) {
  const [secondsLeft, setSecondsLeft] = useState(RESEND_COOLDOWN_SECONDS);

  // The countdown must not run out ahead of the server's own throttle, or the
  // button would offer a resend that Supabase then rejects. See `max_frequency`
  // in supabase/config.toml.
  useEffect(() => {
    if (secondsLeft <= 0) return;
    const id = setTimeout(() => setSecondsLeft((n) => n - 1), 1000);
    return () => clearTimeout(id);
  }, [secondsLeft]);

  if (secondsLeft > 0) {
    return (
      <p className="text-sm text-muted-foreground" aria-live="polite">
        <span>Resend in {secondsLeft}s</span>
      </p>
    );
  }

  return (
    <Button
      type="button"
      variant="outline"
      disabled={isResending}
      onClick={() => {
        onResend();
        setSecondsLeft(RESEND_COOLDOWN_SECONDS);
      }}
    >
      {isResending && <Loader2 className="size-4 animate-spin" />}
      <span>{isResending ? 'Sending…' : 'Resend the link'}</span>
    </Button>
  );
}
