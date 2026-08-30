'use client';

import posthog from 'posthog-js';
import type { MusicProvider } from '@/services/api';

// Every event name lives here so a typo is a compile error instead of a
// phantom PostHog event.

export type ConnectionSurface = 'dashboard' | 'onboarding';

type CaptureOptions = Parameters<typeof posthog.capture>[2];

function capture(
  event: string,
  properties?: Record<string, string | number | boolean>,
  options?: CaptureOptions
): void {
  // When PostHog is unconfigured init never runs; capture would log a
  // console error on every call, so no-op instead (PostHogClient already
  // warns once in dev).
  if (!posthog.__loaded) return;
  posthog.capture(event, properties, options);
}

export function trackMagicLinkRequested(): void {
  capture('auth_magic_link_requested');
}

export function trackProviderConnected(
  provider: MusicProvider,
  properties: { connection_surface: ConnectionSurface; reconnect?: boolean }
): void {
  capture(`${provider}_connected`, {
    connection_surface: properties.connection_surface,
    // Token-renewal reconnects must not count as new connections.
    reconnect: properties.reconnect ?? false,
  });
}

export function trackProviderDisconnected(provider: MusicProvider): void {
  capture(`${provider}_disconnected`);
}

export function trackPlaylistCleaningJobStarted(properties: {
  provider: MusicProvider;
  source_track_count: number;
  uses_custom_name: boolean;
}): void {
  capture('playlist_cleaning_job_started', properties);
}

export function trackAutoSyncEnabled(): void {
  capture('auto_sync_enabled');
}

export function trackAutoSyncDisabled(): void {
  capture('auto_sync_disabled');
}

export function trackSyncCheckRequested(): void {
  capture('sync_check_requested');
}

export function trackSubscriptionCancellationScheduled(): void {
  capture('subscription_cancellation_scheduled');
}

// The two checkout-funnel events fire immediately before a navigation to
// Stripe, which would drop a batched event — hence sendBeacon.
export function trackSubscriptionCheckoutRequested(): void {
  capture('subscription_checkout_requested', undefined, {
    transport: 'sendBeacon',
  });
}

export function trackBillingPortalRequested(): void {
  capture('billing_portal_requested', undefined, { transport: 'sendBeacon' });
}
