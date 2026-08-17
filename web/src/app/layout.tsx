import './styles/globals.css';
import { QueryProvider } from './providers/QueryProvider';
import { ThemeProvider } from './providers/ThemeProvider';
import { Toaster } from 'sonner';
import { Metadata, Viewport } from 'next';
import { Source_Serif_4 } from 'next/font/google';
import PlausibleProvider from 'next-plausible';

/**
 * The display serif that carries the warm editorial direction. Self-hosted at
 * build time by next/font, so there is no CDN request and no flash of fallback
 * text. Variable weight: one file covers every heading size.
 *
 * Chosen for legibility at small sizes — CardTitle and section headings use it,
 * not just hero copy — and for real tabular figures, which the job progress
 * counter ("88 of 187") depends on to avoid digits jittering as it counts up.
 */
const sourceSerif = Source_Serif_4({
  subsets: ['latin'],
  display: 'swap',
  variable: '--font-display-face',
});

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  maximumScale: 5,
  // Matches --background in globals.css so the browser chrome does not flash a
  // colour the page never uses.
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#fbf8f2' },
    { media: '(prefers-color-scheme: dark)', color: '#17140f' },
  ],
};

// A plain object, not generateMetadata(): request-scoped values here (this
// used to spread Sentry.getTraceData()) would either force every route dynamic
// or bake one build-time trace into all pages. The dynamic app routes add
// their own per-request Sentry trace metadata instead.
export const metadata: Metadata = {
  metadataBase: new URL('https://radiowash.com'),
  title: {
    default: 'RadioWash — clean copies of your Apple Music playlists',
    template: '%s | RadioWash',
  },
  description:
    'Make a clean copy of any Apple Music playlist — same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
  applicationName: 'RadioWash',
  keywords: [
    'Apple Music playlist cleaner',
    'clean version playlist',
    'radio edit playlist',
    'remove explicit tracks',
    'family-friendly Apple Music',
    'clean playlist generator',
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
    title: 'RadioWash — clean copies of your Apple Music playlists',
    description:
      'Same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'RadioWash — clean copies of your Apple Music playlists',
    description:
      'Same songs, radio edits substituted, your original untouched. Free to use; requires an Apple Music subscription.',
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
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning className={sourceSerif.variable}>
      <head>
        <PlausibleProvider domain="radiowash.com" />
      </head>
      <body>
        <ThemeProvider
          attribute="class"
          defaultTheme="system"
          enableSystem
          disableTransitionOnChange
        >
          <QueryProvider>{children}</QueryProvider>
          <Toaster position="bottom-right" />
        </ThemeProvider>
      </body>
    </html>
  );
}
