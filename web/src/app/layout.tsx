import './styles/globals.css';
import { QueryProvider } from './providers/QueryProvider';
import { ThemeProvider } from './providers/ThemeProvider';
import { Toaster } from 'sonner';
import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';

export function generateMetadata(): Metadata {
  return {
    ...{
      title: 'RadioWash - The Playlist Washer',
      description: 'The playlist washer',
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
  return (
    <html lang="en" suppressHydrationWarning>
      <body>
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
