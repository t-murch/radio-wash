'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ClientDate } from '@/components/ui/ClientDate';
import {
  ConnectionStatus,
  getConnectionStatus,
  MusicProvider,
  storeProviderTokens,
} from '../services/api';
import { useMusicKit } from '../hooks/useMusicKit';

// Prompt Apple reconnection ahead of the assumed Music User Token expiry. canRefresh is
// meaningless for Apple (no refresh flow exists), so expiry proximity drives the prompt.
const APPLE_RECONNECT_WINDOW_DAYS = 14;

const PROVIDER_LABELS: Record<MusicProvider, string> = {
  spotify: 'Spotify',
  apple_music: 'Apple Music',
};

interface ProviderConnectionStatusProps {
  provider: MusicProvider;
  onConnectionChange?: (connected: boolean) => void;
}

function ProviderIcon({ provider, connected }: { provider: MusicProvider; connected: boolean }) {
  const className = `w-6 h-6 ${connected ? 'text-success' : 'text-muted-foreground'}`;
  if (provider === 'spotify') {
    return (
      <svg className={className} viewBox="0 0 24 24" fill="currentColor">
        <path d="M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.84-.179-.84-.66 0-.36.24-.66.54-.78 4.56-1.021 8.52-.6 11.64 1.32.36.18.48.66.24 1.021zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.481.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.42 1.56-.299.421-1.02.599-1.559.3z" />
      </svg>
    );
  }
  return (
    <svg className={className} viewBox="0 0 24 24" fill="currentColor">
      <path d="M16.365 1.43c0 1.14-.493 2.27-1.177 3.08-.744.9-1.99 1.57-2.987 1.57-.12 0-.23-.02-.3-.03-.01-.06-.04-.22-.04-.39 0-1.15.572-2.27 1.206-2.98.804-.94 2.142-1.64 3.248-1.68.03.13.05.28.05.43zm4.565 15.71c-.03.07-.463 1.58-1.518 3.12-.945 1.34-1.94 2.71-3.43 2.71-1.517 0-1.9-.88-3.63-.88-1.698 0-2.302.91-3.67.91-1.377 0-2.332-1.26-3.428-2.8-1.287-1.82-2.323-4.63-2.323-7.28 0-4.28 2.797-6.55 5.552-6.55 1.448 0 2.675.95 3.6.95.865 0 2.222-1.01 3.902-1.01.613 0 2.886.06 4.374 2.19-.13.09-2.383 1.37-2.383 4.19 0 3.26 2.854 4.42 2.955 4.45z" />
    </svg>
  );
}

export function ProviderConnectionStatus({
  provider,
  onConnectionChange,
}: ProviderConnectionStatusProps) {
  const router = useRouter();
  const label = PROVIDER_LABELS[provider];
  const isApple = provider === 'apple_music';
  // Only the Apple card needs MusicKit; the Spotify card would otherwise pull Apple's CDN
  // script and a developer token it never uses.
  const musicKit = useMusicKit({ enabled: isApple });

  const [status, setStatus] = useState<
    Partial<ConnectionStatus> & { loading: boolean; error?: string }
  >({ connected: false, canRefresh: false, loading: true });
  const [connecting, setConnecting] = useState(false);

  const checkStatus = useCallback(async () => {
    try {
      const result = await getConnectionStatus(provider);
      setStatus({ ...result, loading: false });
      onConnectionChange?.(result.connected);
    } catch (error) {
      console.error(`Failed to check ${provider} connection status:`, error);
      setStatus((prev) => ({
        ...prev,
        loading: false,
        error: 'Failed to check connection status',
      }));
    }
  }, [provider, onConnectionChange]);

  useEffect(() => {
    checkStatus();
  }, [checkStatus]);

  const handleConnect = async () => {
    if (!isApple) {
      // Spotify tokens are (re)issued through Supabase OAuth; the callback syncs them to
      // the API. (Replaces a dead backend /auth/spotify/login link.)
      router.push('/auth');
      return;
    }

    setConnecting(true);
    try {
      // Two distinct failures live here: Apple declining to issue a Music User Token, and our
      // own API declining to store it. Reporting both as "subscription required" sends people
      // to renew a subscription they already have, so keep them apart.
      let musicUserToken: string;
      try {
        musicUserToken = await musicKit.authorize();
      } catch (error) {
        console.error('Apple Music authorization failed:', error);
        setStatus((prev) => ({
          ...prev,
          error:
            'Apple Music authorization failed. An active Apple Music subscription is required.',
        }));
        return;
      }

      await storeProviderTokens('apple_music', musicUserToken);
      await checkStatus();
    } catch (error) {
      console.error('Failed to store Apple Music token:', error);
      setStatus((prev) => ({
        ...prev,
        error:
          'Apple Music approved the connection, but saving it failed. Please try again.',
      }));
    } finally {
      setConnecting(false);
    }
  };

  const needsReconnect = (() => {
    if (!status.connected) return false;
    if (isApple) {
      if (!status.expiresAt) return false;
      const msLeft = new Date(status.expiresAt).getTime() - Date.now();
      return msLeft < APPLE_RECONNECT_WINDOW_DAYS * 24 * 3600 * 1000;
    }
    return !status.canRefresh;
  })();

  if (status.loading) {
    return (
      <div className="bg-card rounded-lg shadow p-6">
        <div className="flex items-center space-x-3">
          <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-success"></div>
          <span className="text-muted-foreground">
            Checking {label} connection...
          </span>
        </div>
      </div>
    );
  }

  const connectDisabled = connecting || (isApple && !musicKit.ready);

  return (
    <div className="bg-card rounded-lg shadow p-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <div
            className={`w-10 h-10 rounded-full flex items-center justify-center ${
              status.connected ? 'bg-success-muted' : 'bg-muted'
            }`}
          >
            <ProviderIcon provider={provider} connected={!!status.connected} />
          </div>
          <div>
            <h3 className="font-medium text-foreground">
              {status.connected ? `${label} Connected` : `${label} Not Connected`}
            </h3>
            <p className="text-sm text-muted-foreground">
              {status.connected ? (
                <>
                  Connected{' '}
                  {status.connectedAt && (
                    <ClientDate
                      date={status.connectedAt}
                      format="toLocaleDateString"
                    />
                  )}
                </>
              ) : (
                `Connect your ${label} account to access playlists`
              )}
            </p>
          </div>
        </div>

        {!status.connected && (
          <button
            onClick={handleConnect}
            disabled={connectDisabled}
            className="px-4 py-2 bg-success text-success-foreground rounded-md hover:bg-success-hover focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-success text-sm font-medium disabled:opacity-50"
          >
            {connecting ? 'Connecting...' : `Connect ${label}`}
          </button>
        )}

        {needsReconnect && (
          <button
            onClick={handleConnect}
            disabled={connectDisabled}
            className="px-4 py-2 bg-muted text-muted-foreground rounded-md hover:bg-accent focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-ring text-sm font-medium disabled:opacity-50"
          >
            Reconnect
          </button>
        )}
      </div>

      {status.error && (
        <div className="mt-4 pt-2">
          <p className="text-sm text-error" role="alert">
            {status.error}
          </p>
        </div>
      )}

      {status.connected && status.lastRefreshAt && (
        <div className="mt-4 pt-4">
          <p className="text-xs text-muted-foreground">
            Last refreshed: <ClientDate date={status.lastRefreshAt} />
          </p>
        </div>
      )}
    </div>
  );
}
