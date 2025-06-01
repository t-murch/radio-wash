import { redirect } from 'next/navigation';

export default function LandingPage() {
  return (
    <section className="min-h-screen bg-gradient-to-br from-green-50 to-blue-50 flex items-center justify-center">
      <div className="max-w-6xl mx-auto px-4 text-center">
        <h1 className="text-5xl font-bold text-gray-900 mb-6">
          Transform Your Explicit Playlists into
          <span className="text-green-600"> Clean Versions</span>
        </h1>
        <p className="text-xl text-gray-600 mb-8 max-w-3xl mx-auto">
          Automatically find clean alternatives for explicit tracks in your
          Spotify playlists. Perfect for family listening, work environments, or
          personal preference.
        </p>
        <button
          className="bg-green-600 text-white px-8 py-4 rounded-lg text-lg font-semibold hover:bg-green-700 transition-colors"
          onClick={() => redirect('/auth')}
        >
          Connect with Spotify - It&apos;s Free
        </button>
      </div>
    </section>
  );
}
