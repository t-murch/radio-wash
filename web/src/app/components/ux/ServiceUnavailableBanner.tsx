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

        <h3 className="font-display text-xl font-semibold text-foreground">
          RadioWash is temporarily unavailable
        </h3>

        <p className="text-muted-foreground max-w-lg">
          Apple Music isn&apos;t responding right now, so sign-in and cleaning
          are paused. Your playlists are unaffected — check back shortly.
        </p>
      </div>
    </div>
  );
}
