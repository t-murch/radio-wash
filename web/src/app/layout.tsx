import './global.css';

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
      <body>{children}</body>
    </html>
  );
}
