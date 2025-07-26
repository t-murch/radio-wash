import './globals.css';
import { QueryProvider } from './providers/QueryProvider';

export const metadata = {
  title: 'RadioWash - The Playlist Washer',
  description: 'The playlist washer',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>
        <QueryProvider>{children}</QueryProvider>
      </body>
    </html>
  );
}
