'use client';

import { useEffect, useState } from 'react';
import { createClient } from '../lib/supabase/client';

const API_BASE_URL =
  (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';

const getSpotifyConnectionStatusClient = async (): Promise<{
  connected: boolean;
  connectedAt?: string;
  lastRefreshAt?: string;
  canRefresh: boolean;
}> => {
  const supabase = createClient();
  const {
    data: { session },
  } = await supabase.auth.getSession();

  if (!session) {
    throw new Error('No active session');
  }

  const response = await fetch(`${API_BASE_URL}/auth/spotify/status`, {
    method: 'GET',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${session.access_token}`,
    },
  });

  if (!response.ok) {
    const errorBody = await response.text();
    console.error(
      `API Error: ${response.status} ${response.statusText}`,
      `Error Body: "${errorBody}"`
    );
    throw new Error(`Request failed: ${response.statusText}`);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.indexOf('application/json') !== -1) {
    return await response.json();
  }
  throw new Error('Invalid response format');
};

interface SpotifyConnectionStatusProps {
  onConnectionChange?: (connected: boolean) => void;
}

export function SpotifyConnectionStatus({
  onConnectionChange,
}: SpotifyConnectionStatusProps) {
  const [status, setStatus] = useState<{
    connected: boolean;
    connectedAt?: string;
    lastRefreshAt?: string;
    canRefresh: boolean;
    loading: boolean;
    error?: string;
  }>({
    connected: false,
    canRefresh: false,
    loading: true,
  });

  useEffect(() => {
    const checkStatus = async () => {
      try {
        const result = await getSpotifyConnectionStatusClient();
        setStatus({
          ...result,
          loading: false,
        });
        onConnectionChange?.(result.connected);
      } catch (error) {
        console.error('Failed to check Spotify connection status:', error);
        setStatus((prev) => ({
          ...prev,
          loading: false,
          error: 'Failed to check connection status',
        }));
      }
    };

    checkStatus();
  }, [onConnectionChange]);

  const handleConnect = () => {
    const apiBaseUrl =
      process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5159';
    window.location.href = `${apiBaseUrl}/api/auth/spotify/login`;
  };

  if (status.loading) {
    return (
      <div className="bg-card rounded-lg shadow p-6">
        <div className="flex items-center space-x-3">
          <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-green-500"></div>
          <span className="text-muted-foreground">
            Checking Spotify connection...
          </span>
        </div>
      </div>
    );
  }

  if (status.error) {
    return (
      <div className="bg-card rounded-lg shadow p-6">
        <div className="flex items-center space-x-3">
          <div className="w-5 h-5 rounded-full bg-error-muted flex items-center justify-center">
            <svg
              className="w-3 h-3 text-red-500"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M6 18L18 6M6 6l12 12"
              />
            </svg>
          </div>
          <span className="text-red-600">{status.error}</span>
        </div>
      </div>
    );
  }

  return (
    <div className="bg-card rounded-lg shadow p-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <div
            className={`w-10 h-10 rounded-full flex items-center justify-center ${
              status.connected ? 'bg-success-muted' : 'bg-muted'
            }`}
          >
            <svg
              className={`w-6 h-6 ${
                status.connected ? 'text-green-500' : 'text-muted-foreground'
              }`}
              viewBox="0 0 24 24"
              fill="currentColor"
            >
              <path d="M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.84-.179-.84-.66 0-.36.24-.66.54-.78 4.56-1.021 8.52-.6 11.64 1.32.36.18.48.66.24 1.021zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.481.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.42 1.56-.299.421-1.02.599-1.559.3z" />
            </svg>
          </div>
          <div>
            <h3 className="font-medium text-foreground">
              {status.connected ? 'Spotify Connected' : 'Spotify Not Connected'}
            </h3>
            <p className="text-sm text-muted-foreground">
              {status.connected
                ? `Connected ${
                    status.connectedAt
                      ? new Date(status.connectedAt).toLocaleDateString()
                      : ''
                  }`
                : 'Connect your Spotify account to access playlists'}
            </p>
          </div>
        </div>

        {!status.connected && (
          <button
            onClick={handleConnect}
            className="px-4 py-2 bg-success text-success-foreground rounded-md hover:bg-success-hover focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-success text-sm font-medium"
          >
            Connect Spotify
          </button>
        )}

        {status.connected && !status.canRefresh && (
          <button
            onClick={handleConnect}
            className="px-4 py-2 bg-muted text-muted-foreground rounded-md hover:bg-accent focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-ring text-sm font-medium"
          >
            Reconnect
          </button>
        )}
      </div>

      {status.connected && status.lastRefreshAt && (
        <div className="mt-4 pt-4">
          <p className="text-xs text-muted-foreground">
            Last refreshed: {new Date(status.lastRefreshAt).toLocaleString()}
          </p>
        </div>
      )}
    </div>
  );
}

