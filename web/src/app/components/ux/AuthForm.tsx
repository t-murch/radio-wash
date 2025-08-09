'use client';

import { useSearchParams } from 'next/navigation';

export function AuthForm({
  signInWithSpotify,
}: {
  signInWithSpotify: () => Promise<void>;
}) {
  const searchParams = useSearchParams();
  const error = searchParams.get('error');

  return (
    <div className="w-full max-w-md p-8 space-y-8 bg-card rounded-xl shadow-md">
      <div className="text-center">
        <h1 className="text-3xl font-extrabold text-foreground">RadioWash</h1>
        <p className="mt-2 text-muted-foreground">
          Create clean versions of your Spotify playlists
        </p>
      </div>

      {error && (
        <div
          className="p-4 text-sm text-red-700 bg-red-100 rounded-lg"
          role="alert"
        >
          {error}
        </div>
      )}

      <form action={signInWithSpotify}>
        <button
          type="submit"
          className="w-full flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-primary-foreground bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
        >
          Connect with Spotify
        </button>
      </form>
    </div>
  );
}
