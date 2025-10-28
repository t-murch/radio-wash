import { Metadata } from 'next';
import LandingPage from './components/ux/LandingPage';
import { StructuredData } from './components/StructuredData';

export const metadata: Metadata = {
  title: 'Clean Your Spotify Playlists - AI-Powered Explicit Content Filter',
  description:
    'Transform explicit Spotify playlists into clean versions with RadioWash. Our AI-powered tool automatically finds clean alternatives for explicit tracks with an 80%+ success rate. Perfect for family listening, work environments, and personal preference. Free to use, no credit card required.',
  keywords: [
    'Spotify playlist cleaner',
    'remove explicit lyrics',
    'clean music filter',
    'family-friendly Spotify',
    'explicit content remover',
    'clean playlist generator',
    'Spotify explicit filter',
    'AI music matching',
    'clean alternatives',
    'work-safe music',
  ],
  alternates: {
    canonical: 'https://radiowash.com',
  },
  openGraph: {
    title: 'RadioWash - Clean Your Spotify Playlists Instantly',
    description:
      'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks. 80%+ success rate, completely free.',
    url: 'https://radiowash.com',
    siteName: 'RadioWash',
    type: 'website',
    locale: 'en_US',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'RadioWash - Clean Your Spotify Playlists Instantly',
    description:
      'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks. 80%+ success rate, completely free.',
  },
};

export default function HomePage() {
  return (
    <>
      <StructuredData />
      <LandingPage />
    </>
  );
}
