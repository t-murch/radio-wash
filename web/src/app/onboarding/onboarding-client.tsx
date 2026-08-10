'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Check, Loader2, Music, X } from 'lucide-react';
import posthog from 'posthog-js';

import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';
import { useMusicKit } from '@/hooks/useMusicKit';
import { storeProviderTokens } from '@/services/api';
import { Stepper } from '../auth/stepper';

/**
 * Why authorization failed. Apple declining to mint a Music User Token and our own
 * API failing to store one look identical to a user but need opposite advice, so
 * they are kept apart rather than collapsed into "something went wrong".
 */
type ConnectFailure =
  | { kind: 'declined' }
  | { kind: 'store-failed' }
  | { kind: 'unavailable'; detail: string };

export function OnboardingClient({
  email,
  appleConnected,
}: {
  email: string;
  appleConnected: boolean;
}) {
  const router = useRouter();
  const musicKit = useMusicKit({ enabled: true });

  const [connected, setConnected] = useState(appleConnected);
  const [connecting, setConnecting] = useState(false);
  const [failure, setFailure] = useState<ConnectFailure | null>(null);

  const handleConnect = async () => {
    setConnecting(true);
    setFailure(null);

    let musicUserToken: string;
    try {
      musicUserToken = await musicKit.authorize();
    } catch (error) {
      // Apple refused. Overwhelmingly this means no active subscription — the one
      // requirement RadioWash cannot work around.
      console.error('Apple Music authorization failed:', error);
      setFailure({ kind: 'declined' });
      setConnecting(false);
      return;
    }

    try {
      await storeProviderTokens('apple_music', musicUserToken);
      posthog.capture('apple_music_connected', {
        connection_surface: 'onboarding',
      });
      setConnected(true);
    } catch (error) {
      // Apple said yes and we lost it. Telling this person to check their
      // subscription would send them to fix something that is not broken.
      console.error('Failed to store the Apple Music token:', error);
      setFailure({ kind: 'store-failed' });
    } finally {
      setConnecting(false);
    }
  };

  if (connected) {
    return <ReadyStep onContinue={() => router.push('/dashboard')} />;
  }

  return (
    <div className="w-full max-w-lg space-y-8">
      <Stepper current={2} />

      <div className="rounded-md border border-border bg-card px-4 py-3 text-sm text-muted-foreground">
        Signed in with your email as{' '}
        <span className="text-foreground">{email}</span> — one step left before
        your first clean playlist.
      </div>

      <div className="space-y-3">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          One more step: connect Apple Music
        </h1>
        <p className="text-muted-foreground">
          Signing in told us who you are. Reading your playlists takes a separate
          permission from Apple — that&apos;s the prompt you&apos;ll see next.
        </p>
      </div>

      <div className="space-y-3 rounded-md border border-border bg-card p-5">
        <h2 className="font-display text-sm font-semibold text-foreground">
          What RadioWash does with this access
        </h2>
        <ul className="space-y-2 text-sm text-muted-foreground">
          <Permission granted>Reads your playlists so you can pick one</Permission>
          <Permission granted>Writes the clean copy back to your library</Permission>
          <Permission granted={false}>Never edits or deletes your originals</Permission>
        </ul>
      </div>

      {failure && <ConnectFailureNotice failure={failure} />}

      {musicKit.error && !failure && (
        <ConnectFailureNotice
          failure={{ kind: 'unavailable', detail: musicKit.error }}
        />
      )}

      <div className="space-y-3">
        <Button
          onClick={handleConnect}
          disabled={connecting || !musicKit.ready}
          className="w-full sm:w-auto"
        >
          {/* Labels in spans: Chrome Translate rewraps bare text nodes in
              <font> tags, and React then crashes swapping the icon next to a
              text node it no longer owns (Sentry JAVASCRIPT-NEXTJS-21/-22). */}
          {connecting ? (
            <>
              <Loader2 className="size-4 animate-spin" />
              <span>Waiting for Apple…</span>
            </>
          ) : (
            <>
              <Music className="size-4" />
              <span>Connect Apple Music</span>
            </>
          )}
        </Button>

        <p className="text-sm text-muted-foreground">
          Apple shows its own permission dialog next. It requires an active Apple
          Music subscription.
        </p>
      </div>
    </div>
  );
}

function Permission({
  granted,
  children,
}: {
  granted: boolean;
  children: React.ReactNode;
}) {
  return (
    <li className="flex items-start gap-2">
      {granted ? (
        <Check className="mt-0.5 size-4 shrink-0 text-success" aria-hidden="true" />
      ) : (
        <X className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      )}
      <span>{children}</span>
    </li>
  );
}

function ConnectFailureNotice({ failure }: { failure: ConnectFailure }) {
  if (failure.kind === 'declined') {
    // Deliberately not styled as an error. Not having a subscription is a fact
    // about the account, not a mistake the person made, and the screen should not
    // read as though they did something wrong.
    return (
      <Alert variant="warning">
        <AlertDescription className="space-y-2">
          <p className="font-medium">
            Apple didn&apos;t grant access to your library.
          </p>
          <p>
            This almost always means the account has no active Apple Music
            subscription. RadioWash works inside your own library, so it needs
            one — there&apos;s no way around it.
          </p>
          <p>
            If you do have a subscription, check you approved the prompt and
            signed in with the right Apple Account, then try again.
          </p>
        </AlertDescription>
      </Alert>
    );
  }

  if (failure.kind === 'store-failed') {
    return (
      <Alert variant="error">
        <AlertDescription className="space-y-2">
          <p className="font-medium">
            Apple approved the connection, but we couldn&apos;t save it.
          </p>
          <p>
            Nothing is wrong with your subscription. Try connecting again — if it
            keeps failing, that&apos;s on our side.
          </p>
        </AlertDescription>
      </Alert>
    );
  }

  return (
    <Alert variant="error">
      <AlertDescription className="space-y-2">
        <p className="font-medium">Apple Music isn&apos;t reachable right now.</p>
        <p>
          The Connect button stays disabled until it is, so you&apos;re not
          clicking into nothing. Details: {failure.detail}
        </p>
      </AlertDescription>
    </Alert>
  );
}

/**
 * Step 3. Deliberately hands the user onward rather than depositing them on a
 * dashboard to work out what to do next.
 */
function ReadyStep({ onContinue }: { onContinue: () => void }) {
  return (
    <div className="w-full max-w-lg space-y-8">
      <Stepper current={3} />

      <div className="space-y-3">
        <h1 className="font-display text-2xl font-semibold text-foreground">
          Apple Music is connected
        </h1>
        <p className="text-muted-foreground">
          Now pick a playlist with explicit tracks in it. RadioWash makes a copy
          with radio edits swapped in and leaves your original exactly as it is.
        </p>
      </div>

      <Separator />

      <div className="space-y-3">
        <Button onClick={onContinue}>Pick a playlist</Button>
        <p className="text-sm text-muted-foreground">
          Cleaning a playlist is free. Most take under a minute.
        </p>
      </div>
    </div>
  );
}
