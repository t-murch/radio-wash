import './styles/globals.css';
import { QueryProvider } from './providers/QueryProvider';
import { ThemeProvider } from './providers/ThemeProvider';
import { Toaster } from 'sonner';
import * as Sentry from '@sentry/nextjs';
import { Metadata, Viewport } from 'next';
import Script from 'next/script';

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  maximumScale: 5,
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#ffffff' },
    { media: '(prefers-color-scheme: dark)', color: '#0a0a0a' },
  ],
};

export function generateMetadata(): Metadata {
  return {
    metadataBase: new URL('https://radiowash.com'),
    title: {
      default: 'RadioWash - Clean Your Spotify Playlists',
      template: '%s | RadioWash',
    },
    description:
      'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks. Perfect for family listening, work environments, and personal preference.',
    applicationName: 'RadioWash',
    keywords: [
      'Spotify',
      'clean playlist',
      'explicit content filter',
      'family-friendly music',
      'clean music',
      'Spotify playlist cleaner',
      'remove explicit lyrics',
      'AI music matching',
    ],
    authors: [{ name: 'RadioWash' }],
    creator: 'RadioWash',
    publisher: 'RadioWash',
    formatDetection: {
      email: false,
      address: false,
      telephone: false,
    },
    openGraph: {
      type: 'website',
      locale: 'en_US',
      url: 'https://radiowash.com',
      siteName: 'RadioWash',
      title: 'RadioWash - Clean Your Spotify Playlists',
      description:
        'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks.',
    },
    twitter: {
      card: 'summary_large_image',
      title: 'RadioWash - Clean Your Spotify Playlists',
      description:
        'Transform explicit Spotify playlists into clean versions. AI-powered tool finds clean alternatives for explicit tracks.',
    },
    robots: {
      index: true,
      follow: true,
      googleBot: {
        index: true,
        follow: true,
        'max-video-preview': -1,
        'max-image-preview': 'large',
        'max-snippet': -1,
      },
    },
    other: {
      ...Sentry.getTraceData(),
    },
  };
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const plausibleDomain = process.env.NEXT_PUBLIC_PLAUSIBLE_DOMAIN || 'radiowash.com';
  const enableAnalytics = process.env.NEXT_PUBLIC_ENABLE_ANALYTICS === 'true';

  return (
    <html lang="en" suppressHydrationWarning>
      <body>
        {enableAnalytics && (
          <Script
            defer
            data-domain={plausibleDomain}
            src="https://plausible.io/js/script.js"
            strategy="afterInteractive"
          />
        )}
        <ThemeProvider
          attribute="class"
          // defaultTheme="system"
          // enableSystem
          disableTransitionOnChange
        >
          <QueryProvider>{children}</QueryProvider>
          <Toaster position="bottom-right" />
        </ThemeProvider>
      </body>
    </html>
  );
}
