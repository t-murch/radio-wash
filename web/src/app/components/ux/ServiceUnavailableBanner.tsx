import { AlertCircle } from 'lucide-react';

export function ServiceUnavailableBanner() {
  return (
    <div
      id="service-unavailable-banner"
      role="alert"
      className="bg-warning/10 border border-warning/20 rounded-lg p-6 my-8 max-w-3xl mx-auto text-center shadow-sm"
    >
      <div className="flex flex-col items-center gap-4">
        <div className="bg-warning/20 p-3 rounded-full">
          <AlertCircle className="w-8 h-8 text-warning" />
        </div>

        <h3 className="text-xl font-bold text-foreground">Service Temporarily Unavailable</h3>

        <p className="text-muted-foreground max-w-lg">
          Due to Spotify API limitations for development applications, we are currently unable to process new user registrations or sync playlists. We are actively working on a resolution.
        </p>
      </div>
    </div>
  );
}
