import { useEffect, useState, useCallback, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import * as signalR from '@microsoft/signalr';
import { HubConnectionState } from '@microsoft/signalr';
import { Job } from '../services/api';
import { toast } from 'sonner';

interface ProgressUpdate {
  progress: number;
  processedTracks: number;
  totalTracks: number;
  currentBatch: string;
  message: string;
}

interface ProgressState extends ProgressUpdate {
  status:
    | 'idle'
    | 'connecting'
    | 'connected'
    | 'processing'
    | 'completed'
    | 'failed';
  estimatedTimeRemaining?: string;
  error?: string;
}

interface UsePlaylistProgressRealtimeReturn {
  progressState: ProgressState;
  isConnected: boolean;
  connectionError?: string;
  reconnect: () => void;
}

/**
 * React hook for managing real-time playlist creation progress updates via SignalR
 *
 * @param jobId - The job ID to subscribe to progress updates for (null to disable)
 * @param authToken - JWT token for authentication
 * @returns Progress state and connection utilities
 */
export function usePlaylistProgressRealtime(
  jobId: string | null,
  authToken?: string
): UsePlaylistProgressRealtimeReturn {
  const queryClient = useQueryClient();
  const [progressState, setProgressState] = useState<ProgressState>({
    status: 'idle',
    progress: 0,
    processedTracks: 0,
    totalTracks: 0,
    currentBatch: '',
    message: 'Waiting to start...',
  });

  const [isConnected, setIsConnected] = useState(false);
  const [connectionError, setConnectionError] = useState<string>();
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const startTimeRef = useRef<number>(Date.now());

  const formatDuration = useCallback((milliseconds: number): string => {
    const seconds = Math.floor(milliseconds / 1000);
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;

    if (minutes > 0) {
      return `${minutes}m ${remainingSeconds}s`;
    }
    return `${remainingSeconds}s`;
  }, []);

  const calculateEstimatedTime = useCallback(
    (progress: number): string | undefined => {
      if (progress <= 0 || progress >= 100) return undefined;

      const elapsed = Date.now() - startTimeRef.current;
      const estimated = (elapsed / progress) * (100 - progress);
      return formatDuration(estimated);
    },
    [formatDuration]
  );

  const reconnect = useCallback(async () => {
    if (connectionRef.current) {
      try {
        await connectionRef.current.start();
      } catch (error) {
        console.error('Failed to reconnect:', error);
        setConnectionError(
          error instanceof Error ? error.message : 'Connection failed'
        );
      }
    }
  }, []);

  useEffect(() => {
    if (!jobId || !authToken) {
      // Clean up existing connection if jobId becomes null
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
      }
      setIsConnected(false);
      setProgressState((prev) => ({ ...prev, status: 'idle' }));
      return;
    }

    console.log('[SignalR] Connecting to job:', jobId);
    console.log('[SignalR] Auth token present:', !!authToken);
    console.log('[SignalR] Auth token length:', authToken?.length || 0);
    startTimeRef.current = Date.now();
    setConnectionError(undefined);

    // Build SignalR connection
    const hubUrl = `${process.env.NEXT_PUBLIC_API_URL}/hubs/playlist-progress`;
    console.log('[SignalR] Connecting to hub URL:', hubUrl);
    console.log('[SignalR] User agent:', navigator.userAgent);

    // Test if the hub endpoint is reachable
    console.log('[SignalR] Testing API reachability...');
    fetch(hubUrl.replace('/hubs/playlist-progress', '/api/healthcheck'))
      .then((response) => {
        console.log(
          '[SignalR] API reachability test SUCCESS:',
          response.status
        );
        console.log(
          '[SignalR] API response headers:',
          Object.fromEntries(response.headers.entries())
        );
      })
      .catch((error) => {
        console.error('[SignalR] API reachability test FAILED:', error);
        console.error('[SignalR] Error details:', {
          message: error.message,
          cause: error.cause,
          stack: error.stack,
        });
      });

    console.log('[SignalR] Building connection with config:', {
      hubUrl,
      hasAuthToken: !!authToken,
      transport: 'ServerSentEvents',
      reconnectDelays: [0, 2000, 10000, 30000],
    });

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => {
          console.log(
            '[SignalR] Auth token factory called, token present:',
            !!authToken
          );
          return authToken;
        },
        // Allow SignalR to negotiate the best transport (WebSocket preferred, fallback to SSE/LongPolling)
        transport:
          signalR.HttpTransportType.WebSockets |
          signalR.HttpTransportType.ServerSentEvents |
          signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    console.log('[SignalR] Connection object created:', {
      state: connection.state,
      baseUrl: connection.baseUrl,
      connectionId: connection.connectionId,
    });

    connectionRef.current = connection;

    // Set up event handlers
    console.log('[SignalR] Setting up event handlers...');

    connection.on('ProgressUpdate', (update: ProgressUpdate) => {
      console.log('[SignalR] ProgressUpdate received:', {
        jobId,
        update,
        timestamp: new Date().toISOString(),
        connectionState: connection.state,
      });
      const estimatedTimeRemaining = calculateEstimatedTime(update.progress);

      setProgressState((prev) => ({
        ...prev,
        ...update,
        status: update.progress >= 100 ? 'completed' : 'processing',
        estimatedTimeRemaining,
      }));
    });

    connection.on(
      'JobCompleted',
      (completedJobId: number, success: boolean, message?: string) => {
        console.log('[SignalR] JobCompleted received:', {
          completedJobId,
          success,
          message,
          currentJobId: jobId,
          timestamp: new Date().toISOString(),
          connectionState: connection.state,
        });
        if (completedJobId.toString() === jobId) {
          toast(`New Playlist Created!`);

          setProgressState((prev) => ({
            ...prev,
            status: 'completed',
            progress: 100,
            message: message || 'Job completed successfully',
            estimatedTimeRemaining: undefined,
          }));

          // Update the job in React Query cache to reflect completion
          queryClient.setQueryData(['jobs'], (oldJobs: Job[] | undefined) => {
            if (!oldJobs) return oldJobs;
            return oldJobs.map((job) =>
              job.id === completedJobId
                ? {
                    ...job,
                    status: 'Completed',
                    processedTracks: job.totalTracks,
                    updatedAt: new Date().toISOString(),
                  }
                : job
            );
          });

          // Invalidate queries to ensure fresh data and show new playlist
          queryClient.invalidateQueries({ queryKey: ['jobs'] });
          queryClient.invalidateQueries({ queryKey: ['playlists'] });

          // Close the connection
          if (connectionRef.current) {
            connectionRef.current.stop();
            connectionRef.current = null;
          }
        }
      }
    );

    connection.on('JobFailed', (failedJobId: number, error: string) => {
      console.log('[SignalR] JobFailed received:', {
        failedJobId,
        error,
        currentJobId: jobId,
        timestamp: new Date().toISOString(),
        connectionState: connection.state,
      });
      if (failedJobId.toString() === jobId) {
        setProgressState((prev) => ({
          ...prev,
          status: 'failed',
          error,
          message: `Job failed: ${error}`,
          estimatedTimeRemaining: undefined,
        }));

        // Update the job in React Query cache to reflect failure
        queryClient.setQueryData(['jobs'], (oldJobs: Job[] | undefined) => {
          if (!oldJobs) return oldJobs;
          return oldJobs.map((job) =>
            job.id === failedJobId
              ? {
                  ...job,
                  status: 'Failed',
                  errorMessage: error,
                  updatedAt: new Date().toISOString(),
                }
              : job
          );
        });

        // Invalidate jobs query to ensure fresh data
        queryClient.invalidateQueries({ queryKey: ['jobs'] });
      }
    });

    // Connection state handlers
    console.log('[SignalR] Setting up connection state handlers...');

    connection.onreconnecting((error) => {
      console.log('[SignalR] Connection RECONNECTING:', {
        error: error?.message,
        timestamp: new Date().toISOString(),
        previousState: connection.state,
      });
      setIsConnected(false);
      setProgressState((prev) => ({ ...prev, status: 'connecting' }));
    });

    connection.onreconnected((connectionId) => {
      console.log('[SignalR] Connection RECONNECTED:', {
        connectionId,
        timestamp: new Date().toISOString(),
        state: connection.state,
      });
      setIsConnected(true);
      setConnectionError(undefined);
      setProgressState((prev) => ({ ...prev, status: 'connected' }));

      // Rejoin the job group after reconnection
      console.log('[SignalR] Rejoining job group after reconnection:', jobId);
      connection
        .invoke('JoinJobGroup', jobId)
        .then(() =>
          console.log('[SignalR] Successfully rejoined job group:', jobId)
        )
        .catch((error) =>
          console.error('[SignalR] Failed to rejoin job group:', error)
        );
    });

    connection.onclose((error) => {
      console.log('[SignalR] Connection CLOSED:', {
        error: error?.message,
        timestamp: new Date().toISOString(),
        wasConnected: isConnected,
      });
      setIsConnected(false);
      if (error) {
        setConnectionError(error.message);
      }
    });

    // Start connection and join job group
    const startConnection = async () => {
      try {
        console.log('[SignalR] STARTING connection...', {
          timestamp: new Date().toISOString(),
          jobId,
          hubUrl,
          initialState: connection.state,
        });
        setProgressState((prev) => ({ ...prev, status: 'connecting' }));

        const startTime = Date.now();
        await connection.start();
        const endTime = Date.now();

        console.log('[SignalR] Connection started SUCCESSFULLY:', {
          duration: `${endTime - startTime}ms`,
          connectionId: connection.connectionId,
          state: connection.state,
          timestamp: new Date().toISOString(),
        });
        setIsConnected(true);
        setProgressState((prev) => ({ ...prev, status: 'connected' }));

        // Join the specific job group to receive updates
        console.log('[SignalR] JOINING job group:', jobId);
        const joinStartTime = Date.now();
        await connection.invoke('JoinJobGroup', jobId);
        const joinEndTime = Date.now();

        console.log('[SignalR] Successfully JOINED job group:', {
          jobId,
          duration: `${joinEndTime - joinStartTime}ms`,
          connectionId: connection.connectionId,
          timestamp: new Date().toISOString(),
        });
      } catch (error) {
        console.error('[SignalR] FAILED to start SignalR connection:', {
          error: error instanceof Error ? error.message : String(error),
          errorStack: error instanceof Error ? error.stack : undefined,
          errorCause: error instanceof Error ? error.cause : undefined,
          jobId,
          hubUrl,
          timestamp: new Date().toISOString(),
          connectionState: connection.state,
        });
        setConnectionError(
          error instanceof Error ? error.message : 'Connection failed'
        );
        setIsConnected(false);
      }
    };

    startConnection();

    // Cleanup function
    return () => {
      console.log('[SignalR] CLEANING UP connection for job:', {
        jobId,
        connectionState: connection?.state,
        timestamp: new Date().toISOString(),
      });

      if (connection && connection.state === HubConnectionState.Connected) {
        console.log('[SignalR] Leaving job group before cleanup:', jobId);
        connection
          .invoke('LeaveJobGroup', jobId)
          .then(() =>
            console.log('[SignalR] Successfully left job group:', jobId)
          )
          .catch((error) =>
            console.error('[SignalR] Failed to leave job group:', error)
          );
      }

      if (connection) {
        console.log('[SignalR] Stopping connection...');
        connection
          .stop()
          .then(() => console.log('[SignalR] Connection stopped successfully'))
          .catch((error) =>
            console.error('[SignalR] Failed to stop connection:', error)
          );
      }
    };
  }, [jobId, authToken, calculateEstimatedTime]);

  return {
    progressState,
    isConnected,
    connectionError,
    reconnect,
  };
}
