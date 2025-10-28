import { useEffect, useState, useCallback, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import * as signalR from '@microsoft/signalr';
import { HubConnectionState } from '@microsoft/signalr';
import { Job } from '../services/api';
import { toast } from 'sonner';
import { logger } from '../lib/logger';

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
        logger.signalR.info('Attempting manual reconnection');
        await connectionRef.current.start();
        logger.signalR.info('Manual reconnection successful');
      } catch (error) {
        logger.signalR.error('Failed to reconnect', error, { 
          connectionState: connectionRef.current?.state 
        });
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

    startTimeRef.current = Date.now();
    setConnectionError(undefined);

    // Build SignalR connection
    const hubUrl = `${process.env.NEXT_PUBLIC_API_URL}/hubs/playlist-progress`;

    // Test if the hub endpoint is reachable
    logger.signalR.debug('Testing API reachability', { 
      hubUrl: hubUrl.replace('/hubs/playlist-progress', '/api/healthcheck'),
      jobId 
    });
    
    fetch(hubUrl.replace('/hubs/playlist-progress', '/api/healthcheck')).catch(
      (error) => {
        logger.signalR.error('API reachability test FAILED', error, {
          jobId,
          healthcheckUrl: hubUrl.replace('/hubs/playlist-progress', '/api/healthcheck')
        });
      }
    );

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => {
          logger.signalR.debug('Auth token factory called', { 
            hasToken: !!authToken,
            jobId 
          });
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

    logger.signalR.debug('SignalR connection created', { 
      hubUrl, 
      jobId,
      transport: 'WebSockets|ServerSentEvents|LongPolling' 
    });

    connectionRef.current = connection;

    connection.on('ProgressUpdate', (update: ProgressUpdate) => {
      logger.signalR.debug('ProgressUpdate received', { 
        jobId,
        progress: update.progress,
        processedTracks: update.processedTracks,
        totalTracks: update.totalTracks,
        currentBatch: update.currentBatch 
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
        logger.signalR.info('JobCompleted received', { 
          completedJobId,
          success,
          message,
          currentJobId: jobId 
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
      logger.signalR.error('JobFailed received', new Error(error), { 
        failedJobId,
        currentJobId: jobId 
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

    connection.onreconnecting((error) => {
      logger.signalR.warn('Connection is reconnecting', { 
        jobId,
        error: error?.message 
      });
      setIsConnected(false);
      setProgressState((prev) => ({ ...prev, status: 'connecting' }));
    });

    connection.onreconnected((connectionId) => {
      logger.signalR.info('Connection reconnected successfully', { 
        connectionId,
        jobId 
      });
      setIsConnected(true);
      setConnectionError(undefined);
      setProgressState((prev) => ({ ...prev, status: 'connected' }));

      // Rejoin the job group after reconnection
      connection
        .invoke('JoinJobGroup', jobId)
        .then(() => {
          logger.signalR.info('Successfully rejoined job group after reconnection', { jobId });
        })
        .catch((error) => {
          logger.signalR.error('Failed to rejoin job group after reconnection', error, { jobId });
        });
    });

    connection.onclose((error) => {
      logger.signalR.info('Connection closed', { 
        jobId,
        hasError: !!error,
        errorMessage: error?.message 
      });
      setIsConnected(false);
      if (error) {
        setConnectionError(error.message);
      }
    });

    // Start connection and join job group
    const startConnection = async () => {
      try {
        logger.signalR.info('Starting SignalR connection', { 
          jobId, 
          hubUrl 
        });
        setProgressState((prev) => ({ ...prev, status: 'connecting' }));

        await connection.start();

        logger.signalR.info('SignalR connection started successfully', { 
          jobId,
          connectionState: connection.state 
        });
        setIsConnected(true);
        setProgressState((prev) => ({ ...prev, status: 'connected' }));

        // Join the specific job group to receive updates
        await connection.invoke('JoinJobGroup', jobId);
        logger.signalR.info('Successfully joined job group', { jobId });
      } catch (error) {
        logger.signalR.error('FAILED to start SignalR connection', error, {
          jobId,
          hubUrl,
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
      logger.signalR.debug('Cleaning up SignalR connection', { 
        jobId,
        connectionState: connection?.state 
      });

      if (connection && connection.state === HubConnectionState.Connected) {
        connection
          .invoke('LeaveJobGroup', jobId)
          .then(() => {
            logger.signalR.debug('Successfully left job group during cleanup', { jobId });
          })
          .catch((error) => {
            logger.signalR.error('Failed to leave job group during cleanup', error, { jobId });
          });
      }

      if (connection) {
        connection
          .stop()
          .then(() => {
            logger.signalR.debug('Connection stopped successfully during cleanup', { jobId });
          })
          .catch((error) => {
            logger.signalR.error('Failed to stop connection during cleanup', error, { jobId });
          });
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
