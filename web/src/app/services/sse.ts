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

class SSEService {
  private eventSources: Map<number, EventSource> = new Map();
  private listeners: Map<string, ((data: any) => void)[]> = new Map();

  connect(jobId: number): void {
    if (this.eventSources.has(jobId)) {
      return; // Already connected to this job
    }

    const API_BASE_URL = (process.env.NEXT_PUBLIC_API_URL || 'http://127.0.0.1:5159') + '/api';
    const eventSource = new EventSource(`${API_BASE_URL}/jobevents/${jobId}`, {
      withCredentials: true
    });

    eventSource.onopen = () => {
      console.log(`SSE connected to job ${jobId}`);
    };

    eventSource.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        this.emit('message', data);
      } catch (error) {
        console.error('Failed to parse SSE message:', error);
      }
    };

    eventSource.addEventListener('job-update', (event) => {
      try {
        const update: JobUpdate = JSON.parse(event.data);
        this.emit('job-update', update);
      } catch (error) {
        console.error('Failed to parse job update:', error);
      }
    });

    eventSource.addEventListener('track-processed', (event) => {
      try {
        const track: TrackProcessed = JSON.parse(event.data);
        this.emit('track-processed', track);
      } catch (error) {
        console.error('Failed to parse track processed:', error);
      }
    });

    eventSource.onerror = (error) => {
      console.error(`SSE error for job ${jobId}:`, error);
      this.disconnect(jobId);
    };

    this.eventSources.set(jobId, eventSource);
  }

  disconnect(jobId: number): void {
    const eventSource = this.eventSources.get(jobId);
    if (eventSource) {
      eventSource.close();
      this.eventSources.delete(jobId);
      console.log(`SSE disconnected from job ${jobId}`);
    }
  }

  disconnectAll(): void {
    this.eventSources.forEach((eventSource, jobId) => {
      eventSource.close();
      console.log(`SSE disconnected from job ${jobId}`);
    });
    this.eventSources.clear();
    this.listeners.clear();
  }

  onJobUpdate(callback: (update: JobUpdate) => void): void {
    this.addListener('job-update', callback);
  }

  onTrackProcessed(callback: (track: TrackProcessed) => void): void {
    this.addListener('track-processed', callback);
  }

  offJobUpdate(callback: (update: JobUpdate) => void): void {
    this.removeListener('job-update', callback);
  }

  offTrackProcessed(callback: (track: TrackProcessed) => void): void {
    this.removeListener('track-processed', callback);
  }

  private addListener(event: string, callback: (data: any) => void): void {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, []);
    }
    this.listeners.get(event)!.push(callback);
  }

  private removeListener(event: string, callback: (data: any) => void): void {
    const listeners = this.listeners.get(event);
    if (listeners) {
      const index = listeners.indexOf(callback);
      if (index > -1) {
        listeners.splice(index, 1);
      }
    }
  }

  private emit(event: string, data: any): void {
    const listeners = this.listeners.get(event);
    if (listeners) {
      listeners.forEach(callback => callback(data));
    }
  }

  isConnected(jobId: number): boolean {
    const eventSource = this.eventSources.get(jobId);
    return eventSource?.readyState === EventSource.OPEN;
  }
}

export const sseService = new SSEService();