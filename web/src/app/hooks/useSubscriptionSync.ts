'use client';

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getSubscriptionStatus,
  getCurrentSubscription,
  enableSyncForJob,
  subscribeToSync,
  createPortalSession,
  getSyncConfigs,
  type SubscriptionStatus,
  type UserSubscriptionDto,
  type PlaylistSyncConfig
} from '../services/api';

export const useSubscriptionStatus = () => {
  return useQuery<SubscriptionStatus>({
    queryKey: ['subscription-status'],
    queryFn: getSubscriptionStatus,
  });
};

export const useCurrentSubscription = () => {
  return useQuery<UserSubscriptionDto | null>({
    queryKey: ['current-subscription'],
    queryFn: getCurrentSubscription,
  });
};

export const useEnableSyncForJob = () => {
  const queryClient = useQueryClient();
  
  return useMutation<PlaylistSyncConfig, Error, number>({
    mutationFn: enableSyncForJob,
    onSuccess: () => {
      // Invalidate and refetch subscription status and sync configs
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
      queryClient.invalidateQueries({ queryKey: ['sync-configs'] });
    },
  });
};

export const useSubscribeToSync = () => {
  const queryClient = useQueryClient();

  return useMutation<{ checkoutUrl: string }, Error>({
    mutationFn: subscribeToSync,
    onSuccess: (data) => {
      // Redirect to Stripe checkout
      window.location.href = data.checkoutUrl;
    },
  });
};

// Opens the Stripe Customer Portal so users can update payment methods, download
// invoices, and cancel the subscription outside of our own cancel flow. The API
// returns a one-time portal URL; we navigate away immediately so React Query
// cache invalidation isn't needed — the subscription page will refetch on return.
export const useCreatePortalSession = () => {
  return useMutation<{ portalUrl: string }, Error>({
    mutationFn: createPortalSession,
    onSuccess: (data) => {
      window.location.assign(data.portalUrl);
    },
  });
};

export const useSyncConfigs = () => {
  return useQuery<PlaylistSyncConfig[]>({
    queryKey: ['sync-configs'],
    queryFn: getSyncConfigs,
  });
};