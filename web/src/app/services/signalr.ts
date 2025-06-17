import * as signalR from '@microsoft/signalr';

export interface JobUpdate {
  jobId: number;
  status: string;
  processedTracks: number;
  totalTracks: number;
  matchedTracks: number;
  errorMessage?: string;
  updatedAt: string;
}

export interface TrackProcessed {
  jobId: number;
  sourceTrackName: string;
  sourceArtistName: string;
  isExplicit: boolean;
  hasCleanMatch: boolean;
  targetTrackName?: string;
  targetArtistName?: string;
}

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private connectionPromise: Promise<void> | null = null;

  async connect(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    const hubUrl = `${
      process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159'
    }/hubs/job-status`;

    const logLevel =
      process.env.NODE_ENV === 'production'
        ? signalR.LogLevel.Warning
        : signalR.LogLevel.Information;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        withCredentials: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(logLevel)
      .build();

    // Connection state logging
    this.connection.onreconnecting(() =>
      console.debug('SignalR: Reconnecting...')
    );
    this.connection.onreconnected(() => console.log('SignalR: Reconnected'));
    this.connection.onclose(() => console.debug('SignalR: Connection closed'));

    this.connectionPromise = this.connection.start();
    await this.connectionPromise;
    console.debug('SignalR: Connected');
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.connectionPromise = null;
    }
  }

  async subscribeToJob(jobId: number): Promise<void> {
    await this.ensureConnected();
    await this.connection!.invoke('SubscribeToJob', jobId);
  }

  async unsubscribeFromJob(jobId: number): Promise<void> {
    await this.ensureConnected();
    await this.connection!.invoke('UnsubscribeFromJob', jobId);
  }

  onJobStatusChanged(callback: (update: JobUpdate) => void): void {
    this.connection?.on('JobStatusChanged', callback);
  }

  onJobProgressUpdate(callback: (update: JobUpdate) => void): void {
    this.connection?.on('JobProgressUpdate', callback);
  }

  onJobCompleted(callback: (update: JobUpdate) => void): void {
    this.connection?.on('JobCompleted', callback);
  }

  onJobFailed(callback: (update: JobUpdate) => void): void {
    this.connection?.on('JobFailed', callback);
  }

  onTrackProcessed(callback: (track: TrackProcessed) => void): void {
    this.connection?.on('TrackProcessed', callback);
  }

  removeAllListeners(): void {
    this.connection?.off('JobStatusChanged');
    this.connection?.off('JobProgressUpdate');
    this.connection?.off('JobCompleted');
    this.connection?.off('JobFailed');
    this.connection?.off('TrackProcessed');
  }

  private async ensureConnected(): Promise<void> {
    if (this.connectionPromise) {
      await this.connectionPromise;
    }
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      throw new Error('SignalR connection is not established');
    }
  }
}

export const signalRService = new SignalRService();
