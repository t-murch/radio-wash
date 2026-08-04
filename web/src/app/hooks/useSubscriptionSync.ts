'use client';

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  getSubscriptionStatus, 
  getCurrentSubscription,
  enableSyncForJob, 
  subscribeToSync,
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
  return useMutation<{ checkoutUrl: string }, Error>({
    mutationFn: async () => {
      const data = await subscribeToSync();
      if (!data?.checkoutUrl) {
        throw new Error('Checkout could not be started. Please try again.');
      }
      return { checkoutUrl: data.checkoutUrl };
    },
    // Never retry: each attempt creates a new Stripe checkout session.
    retry: false,
    onSuccess: (data) => {
      // Redirect to Stripe checkout
      window.location.href = data.checkoutUrl;
    },
  });
};

export const useSyncConfigs = () => {
  return useQuery<PlaylistSyncConfig[]>({
    queryKey: ['sync-configs'],
    queryFn: getSyncConfigs,
  });
};