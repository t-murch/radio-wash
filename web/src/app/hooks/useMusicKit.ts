'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { getMusicKitDeveloperToken } from '../services/api';

const MUSICKIT_SCRIPT_URL = 'https://js-cdn.music.apple.com/musickit/v3/musickit.js';
const MUSICKIT_SCRIPT_ID = 'musickit-js';

interface MusicKitInstance {
  authorize: () => Promise<string>;
  unauthorize: () => Promise<void>;
  isAuthorized: boolean;
}

interface MusicKitGlobal {
  configure: (config: {
    developerToken: string;
    app: { name: string; build: string };
  }) => Promise<MusicKitInstance>;
  getInstance: () => MusicKitInstance;
}

declare global {
  interface Window {
    MusicKit?: MusicKitGlobal;
  }
}

// Idempotently injects the MusicKit v3 script and resolves when the global is available.
const loadMusicKitScript = (): Promise<MusicKitGlobal> =>
  new Promise((resolve, reject) => {
    if (window.MusicKit) {
      resolve(window.MusicKit);
      return;
    }

    const onLoaded = () => {
      if (window.MusicKit) {
        resolve(window.MusicKit);
      } else {
        reject(new Error('MusicKit script loaded but global is missing'));
      }
    };

    // The script dispatches `musickitloaded` on the document once initialized.
    document.addEventListener('musickitloaded', onLoaded, { once: true });

    const onError = () => {
      document.removeEventListener('musickitloaded', onLoaded);
      // Remove the dead tag so a later mount injects a fresh one. Left in place, the next
      // caller would find the existing element, wait on a `musickitloaded` that already
      // failed to fire, and hang forever.
      document.getElementById(MUSICKIT_SCRIPT_ID)?.remove();
      reject(new Error('Failed to load MusicKit script'));
    };

    const existing = document.getElementById(MUSICKIT_SCRIPT_ID);
    if (existing) {
      // A previous mount already injected the tag. If that load failed, `musickitloaded`
      // will never fire, so attach to the existing element rather than waiting forever.
      existing.addEventListener('error', onError, { once: true });
      return;
    }

    const script = document.createElement('script');
    script.id = MUSICKIT_SCRIPT_ID;
    script.src = MUSICKIT_SCRIPT_URL;
    script.async = true;
    script.addEventListener('error', onError, { once: true });
    document.head.appendChild(script);
  });

/**
 * Loads MusicKit JS, configures it with the developer token from the API, and exposes
 * `authorize` to mint a Music User Token. `authorize` opens Apple's consent popup and must
 * be invoked from a user gesture (click handler).
 *
 * Pass `enabled: false` for non-Apple callers to skip the CDN script and the developer-token
 * request entirely; the hook is still called unconditionally, satisfying the Rules of Hooks.
 */
export function useMusicKit({ enabled = true }: { enabled?: boolean } = {}) {
  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const instanceRef = useRef<MusicKitInstance | null>(null);

  useEffect(() => {
    if (!enabled) return;

    let cancelled = false;

    const setup = async () => {
      try {
        const [musicKit, { token }] = await Promise.all([
          loadMusicKitScript(),
          getMusicKitDeveloperToken(),
        ]);
        const instance = await musicKit.configure({
          developerToken: token,
          app: { name: 'RadioWash', build: '1.0.0' },
        });
        if (!cancelled) {
          instanceRef.current = instance;
          setReady(true);
        }
      } catch (err) {
        console.error('Failed to initialize MusicKit:', err);
        if (!cancelled) {
          setError(
            err instanceof Error ? err.message : 'Failed to initialize Apple Music'
          );
        }
      }
    };

    setup();
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  const authorize = useCallback(async (): Promise<string> => {
    if (!instanceRef.current) {
      throw new Error('MusicKit is not ready');
    }
    // Resolves with the Music User Token once the user grants access.
    return instanceRef.current.authorize();
  }, []);

  return { ready, error, authorize };
}
