'use client';

import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getSubscriptionStatus,
  getCurrentSubscription,
  enableSyncForJob,
  subscribeToSync,
  getSyncConfigs,
  verifyCheckoutSession,
  type SubscriptionStatus,
  type UserSubscriptionDto,
  type PlaylistSyncConfig,
  type CheckoutVerification,
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

export const useVerifyCheckoutSession = (sessionId: string | null) => {
  const [attemptCount, setAttemptCount] = useState(0);
  const maxAttempts = 15;

  const query = useQuery<CheckoutVerification>({
    queryKey: ['verify-checkout-session', sessionId],
    queryFn: () => verifyCheckoutSession(sessionId!),
    enabled: !!sessionId && attemptCount < maxAttempts,
    refetchInterval: (query) => {
      if (query.state.data?.verified) return false;
      if (attemptCount >= maxAttempts) return false;
      return 2000;
    },
  });

  useEffect(() => {
    if (query.dataUpdatedAt) {
      setAttemptCount((prev) => prev + 1);
    }
  }, [query.dataUpdatedAt]);

  const isVerified = query.data?.verified ?? false;
  const isTimeout = !isVerified && attemptCount >= maxAttempts;
  const isLoading = !isVerified && !isTimeout && query.isLoading;

  return {
    isVerified,
    isLoading,
    subscription: query.data?.subscription,
    isTimeout,
  };
};

export const useSyncConfigs = () => {
  return useQuery<PlaylistSyncConfig[]>({
    queryKey: ['sync-configs'],
    queryFn: getSyncConfigs,
  });
};