import { ImageResponse } from 'next/og';

export const runtime = 'edge';

export const alt = 'RadioWash — a clean copy of any Apple Music playlist';
export const size = {
  width: 1200,
  height: 630,
};
export const contentType = 'image/png';

// Literal hex throughout: the Edge runtime has no access to the CSS custom
// properties in globals.css. These track the light-mode warm editorial palette —
// background, foreground, primary, muted-foreground — and must be updated together.
const BACKGROUND = '#fbf8f2';
const FOREGROUND = '#221e17';
const PRIMARY = '#0f5f5c';
const MUTED = '#6f6759';

export default async function Image() {
  return new ImageResponse(
    (
      <div
        style={{
          height: '100%',
          width: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-start',
          justifyContent: 'center',
          backgroundColor: BACKGROUND,
          padding: '90px',
        }}
      >
        <div
          style={{
            fontSize: 88,
            fontWeight: 600,
            fontFamily: 'Georgia, serif',
            color: FOREGROUND,
            letterSpacing: '-0.02em',
          }}
        >
          RadioWash
        </div>

        <div
          style={{
            fontSize: 44,
            fontFamily: 'Georgia, serif',
            color: FOREGROUND,
            marginTop: 24,
            maxWidth: 900,
            lineHeight: 1.3,
          }}
        >
          A clean copy of any Apple Music playlist.
        </div>

        {/* The single teal mark on the page, echoing the product's one accent. */}
        <div
          style={{
            width: 120,
            height: 4,
            backgroundColor: PRIMARY,
            marginTop: 48,
            marginBottom: 40,
          }}
        />

        <div
          style={{
            fontSize: 30,
            color: MUTED,
            letterSpacing: '0.01em',
          }}
        >
          Same songs · radio edits · original untouched
        </div>

        <div
          style={{
            fontSize: 26,
            color: MUTED,
            marginTop: 'auto',
          }}
        >
          radiowash.com
        </div>
      </div>
    ),
    {
      ...size,
    }
  );
}
