'use client';

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  getSubscriptionStatus, 
  enableSyncForJob, 
  subscribeToSync,
  getSyncConfigs,
  type SubscriptionStatus,
  type PlaylistSyncConfig 
} from '../services/api';

export const useSubscriptionStatus = () => {
  return useQuery<SubscriptionStatus>({
    queryKey: ['subscription-status'],
    queryFn: getSubscriptionStatus,
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
  
  return useMutation<{ success: boolean; subscriptionId?: string }, Error>({
    mutationFn: subscribeToSync,
    onSuccess: () => {
      // Invalidate subscription status after successful subscription
      queryClient.invalidateQueries({ queryKey: ['subscription-status'] });
    },
  });
};

export const useSyncConfigs = () => {
  return useQuery<PlaylistSyncConfig[]>({
    queryKey: ['sync-configs'],
    queryFn: getSyncConfigs,
  });
};