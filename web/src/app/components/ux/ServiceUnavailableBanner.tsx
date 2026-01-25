import { AlertCircle, Music } from 'lucide-react';

export function ServiceUnavailableBanner() {
  return (
    <div className="bg-warning/10 border border-warning/20 rounded-lg p-6 my-8 max-w-3xl mx-auto text-center shadow-sm">
      <div className="flex flex-col items-center gap-4">
        <div className="bg-warning/20 p-3 rounded-full">
          <AlertCircle className="w-8 h-8 text-warning" />
        </div>
        
        <h3 className="text-xl font-bold text-foreground">Service Temporarily Unavailable</h3>
        
        <p className="text-muted-foreground max-w-lg">
          Due to Spotify API limitations for development applications, we are currently unable to process new user registrations or sync playlists. We are actively working on a resolution.
        </p>

        <div className="mt-4 pt-4 border-t border-warning/10 w-full flex flex-col items-center">
           <p className="text-sm font-medium text-muted-foreground mb-2">Coming Soon</p>
           <div className="flex items-center gap-2 px-3 py-1.5 bg-background rounded-full border shadow-sm text-sm">
             <Music className="w-4 h-4 text-brand" />
             <span className="font-medium">Apple Music Support</span>
           </div>
        </div>
      </div>
    </div>
  );
}
