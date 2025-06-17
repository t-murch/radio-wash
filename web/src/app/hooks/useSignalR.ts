import { useEffect, useState } from 'react';
import { signalRService, JobUpdate, TrackProcessed } from '@/services/signalr';

export function useSignalR(token: string | null) {
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    if (!token) return;

    const connect = async () => {
      try {
        await signalRService.connect();
        setIsConnected(true);
      } catch (error) {
        console.error('Failed to connect to SignalR:', error);
        setIsConnected(false);
      }
    };

    connect();

    return () => {
      signalRService.removeAllListeners();
      signalRService.disconnect();
      setIsConnected(false);
    };
  }, [token]);

  return { isConnected, signalRService };
}

export function useJobUpdates(
  jobId: number | null,
  onUpdate: (update: JobUpdate) => void,
  onTrackProcessed?: (track: TrackProcessed) => void
) {
  useEffect(() => {
    if (!jobId) return;

    // Subscribe to job
    signalRService.subscribeToJob(jobId).catch(console.error);

    // Set up event handlers
    signalRService.onJobStatusChanged(onUpdate);
    signalRService.onJobProgressUpdate(onUpdate);
    signalRService.onJobCompleted(onUpdate);
    signalRService.onJobFailed(onUpdate);

    if (onTrackProcessed) {
      signalRService.onTrackProcessed(onTrackProcessed);
    }

    return () => {
      signalRService.unsubscribeFromJob(jobId).catch(console.error);
    };
  }, [jobId, onUpdate, onTrackProcessed]);
}
